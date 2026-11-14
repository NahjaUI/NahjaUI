using System;
using System.Collections.Generic;

namespace NahjaUI.Game;

/// <summary>
/// Streamer mode: the names of player characters are replaced, in everything NahjaUI draws, by the names of famous characters of the
/// Final Fantasy saga. The same person always gets the same name (every frame, nameplate and tooltip agree), and the same real name gets
/// the same invented name in every session. Only what NahjaUI draws changes: the game's own windows (chat, its own lists) are not touched.
/// </summary>
internal static class StreamerMode
{
    private static readonly string[] Men =
    {
        "Cloud Strife", "Sephiroth", "Squall Leonhart", "Zidane Tribal", "Tidus", "Auron", "Wakka", "Vaan", "Balthier", "Basch",
        "Noctis Caelum", "Prompto Argentum", "Ignis Scientia", "Gladiolus Amicitia", "Locke Cole", "Edgar Figaro", "Sabin Figaro",
        "Cecil Harvey", "Kain Highwind", "Bartz Klauser", "Firion", "Vivi Ornitier", "Steiner", "Barret Wallace", "Vincent Valentine",
        "Zack Fair", "Cid Highwind", "Snow Villiers", "Sazh Katzroy", "Hope Estheim", "Cor Leonis", "Alphinaud Leveilleur",
        "Thancred Waters", "Estinien Wyrmblood", "Haurchefant Greystone", "Emet-Selch", "Urianger Augurelt", "Kefka Palazzo",
        "Seymour Guado", "Gilgamesh"
    };

    private static readonly string[] Women =
    {
        "Tifa Lockhart", "Aerith Gainsborough", "Yuffie Kisaragi", "Rinoa Heartilly", "Yuna", "Lulu", "Rikku", "Garnet Alexandros",
        "Freya Crescent", "Beatrix", "Eiko Carol", "Quina Quen", "Ashe Dalmasca", "Fran", "Penelo", "Lightning Farron", "Serah Farron",
        "Vanille Dia", "Fang", "Terra Branford", "Celes Chere", "Rosa Farrell", "Rydia", "Lenna Tycoon", "Faris Scherwiz",
        "Maria", "Lunafreya Fleuret", "Aranea Highwind", "Cindy Aurum", "Alisaie Leveilleur", "Y'shtola Rhul", "Krile Mayer Baldesion",
        "Minfilia Warde", "Ultimecia", "Edea Kramer", "Quistis Trepe", "Selphie Tilmitt", "Relm Arrowny", "Lilith", "Tellah"
    };

    private static readonly string[] Names = BuildNames();

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Used = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    // Men and women one after the other, so any run of names is a mix of both.
    private static string[] BuildNames()
    {
        var all = new List<string>();
        for (var i = 0; i < Math.Max(Men.Length, Women.Length); i++)
        {
            if (i < Men.Length)
                all.Add(Men[i]);

            if (i < Women.Length)
                all.Add(Women[i]);
        }

        return all.ToArray();
    }

    /// <summary>Set every frame from the configuration.</summary>
    public static bool Enabled { get; set; }

    private static int seed;

    /// <summary>Set every frame from the configuration. A different number gives every real name a different invented one.</summary>
    public static int Seed
    {
        get => seed;
        set
        {
            if (value == seed)
                return;

            lock (Gate)
            {
                seed = value;
                Aliases.Clear();
                Used.Clear();
            }
        }
    }

    /// <summary>The name to show: the real one, or its alias when streamer mode is on and the unit is a player character.</summary>
    public static string Name(string name, bool isPlayer)
    {
        if (!Enabled || !isPlayer || string.IsNullOrWhiteSpace(name))
            return name;

        // In a Frontline the game does not give the name of an enemy player, only its job: that is not a name to hide.
        if (IsJobName(name))
            return name;

        lock (Gate)
        {
            if (!Aliases.TryGetValue(name, out var alias))
            {
                alias = MakeAlias(name);
                Aliases[name] = alias;
                Used.Add(alias);
            }

            return alias;
        }
    }

    // The names of the jobs (in the language of the game and in English). A real character name always has a first and a last name, so none of them can be one.
    private static HashSet<string>? jobNames;

    private static bool IsJobName(string name)
    {
        lock (Gate)
        {
            if (jobNames is null)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>())
                {
                    var local = row.Name.ToString();
                    if (local.Length > 0)
                        names.Add(local);

                    var english = row.NameEnglish.ToString();
                    if (english.Length > 0)
                        names.Add(english);
                }

                jobNames = names;
            }

            return jobNames.Contains(name.Trim());
        }
    }

    /// <summary>Is this text one of the names given by streamer mode?</summary>
    public static bool IsAlias(string name)
    {
        if (!Enabled)
            return false;

        lock (Gate)
            return Used.Contains(name);
    }

    // The name for a real one: always the same for the same text. A name already given to someone else is skipped while there are free ones;
    // once they run out the names repeat (there are never many on the screen).
    private static string MakeAlias(string real)
    {
        var seed = Hash(real);
        var start = (int)(seed % (uint)Names.Length);

        for (var i = 0; i < Names.Length; i++)
        {
            var candidate = Names[(start + i) % Names.Length];
            if (!Used.Contains(candidate))
                return candidate;
        }

        return Names[start];
    }

    // FNV-1a: the same text gives the same number in every session (string.GetHashCode does not).
    private static uint Hash(string text)
    {
        var hash = 2166136261u ^ ((uint)seed * 2654435761u);
        foreach (var c in text)
            hash = (hash ^ c) * 16777619u;

        return hash;
    }
}
