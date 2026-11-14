using System;
using System.Globalization;

namespace NahjaUI.Modules.HudBars;

/// <summary>Turns names, HP and numbers into the text a frame shows, following the user's settings.</summary>
internal static class UnitTextFormatter
{
    // ---------------------------------------------------------------- names

    public static string FormatName(string name, NameFormat format, bool isPlayer, bool onlyPlayers)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        if (format == NameFormat.Full || (onlyPlayers && !isPlayer))
            return name;

        // A player has a first and a last name. Anything with a single word has nothing to cut.
        var space = name.IndexOf(' ');
        if (space <= 0 || space >= name.Length - 1)
            return name;

        var first = name[..space];
        var last = name[(space + 1)..].Trim();

        var firstInitial = char.ToUpperInvariant(first[0]);
        var lastInitial = char.ToUpperInvariant(last[0]);

        return format switch
        {
            NameFormat.FirstName => first,
            NameFormat.LastName => last,
            NameFormat.FirstInitialLastName => $"{firstInitial}. {last}",
            NameFormat.FirstNameLastInitial => $"{first} {lastInitial}.",
            NameFormat.Initials => $"{firstInitial}. {lastInitial}.",
            _ => name
        };
    }

    // ---------------------------------------------------------------- HP

    // Between the number and the percentage in the presets that show both.
    private const string Separator = " | ";

    public static string FormatValue(IValueTextFormat settings, uint current, uint max)
    {
        if (settings.HpPreset == HpTextPreset.Deficit && current >= max)
            return string.Empty;

        var template = settings.HpPreset switch
        {
            HpTextPreset.CurrentAndPercent => "{curk}" + Separator + "{pct}",
            HpTextPreset.CurrentFullAndPercent => "{cur}" + Separator + "{pct}",
            HpTextPreset.Current => "{curk}",
            HpTextPreset.CurrentFull => "{cur}",
            HpTextPreset.Percent => "{pct}",
            HpTextPreset.CurrentAndMax => "{curk} / {maxk}",
            HpTextPreset.Deficit => "-{defk}",
            _ => settings.CustomTemplate
        };

        var missing = max > current ? max - current : 0u;

        return template
            .Replace("{curk}", Abbreviate(current, settings))
            .Replace("{maxk}", Abbreviate(max, settings))
            .Replace("{defk}", Abbreviate(missing, settings))
            .Replace("{cur}", Full(current))
            .Replace("{max}", Full(max))
            .Replace("{def}", Full(missing))
            .Replace("{pct}", Percent(current, max, settings));
    }

    /// <summary>The whole number with a comma between groups of three digits (104,071).</summary>
    public static string Full(uint value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>738,000 as 738K and 1,250,000 as 1.2M.</summary>
    public static string Abbreviate(uint value, IValueTextFormat settings)
    {
        var decimals = Math.Clamp(settings.AbbreviationDecimals, 0, 2);
        string text;

        if (value >= 1_000_000)
        {
            // Millions always keep a decimal, or 1.9M would read as 2M.
            text = Format(value / 1_000_000.0, Math.Max(decimals, 1)) + "M";
        }
        else if (value >= 1_000)
        {
            var thousands = Math.Round(value / 1_000.0, decimals);

            // 999,999 rounds to 1000K: write it as millions instead.
            text = thousands >= 1000.0
                ? Format(value / 1_000_000.0, Math.Max(decimals, 1)) + "M"
                : Format(thousands, decimals) + "K";
        }
        else
        {
            text = value.ToString(CultureInfo.InvariantCulture);
        }

        return text;
    }

    // The percentage is rounded down, so a HP that is not full never reads 100 %.
    public static string Percent(uint current, uint max, IValueTextFormat settings)
    {
        var decimals = Math.Clamp(settings.PercentDecimals, 0, 2);
        var percent = max == 0 ? 0.0 : Math.Min(current * 100.0 / max, 100.0);

        var factor = Math.Pow(10.0, decimals);
        var floored = Math.Floor(percent * factor) / factor;

        return Format(floored, decimals) + "%";
    }

    private static string Format(double value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.InvariantCulture);
}
