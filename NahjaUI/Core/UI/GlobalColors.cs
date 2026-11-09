using System;
using System.Numerics;
using Newtonsoft.Json;

namespace NahjaUI.Core.Ui;

/// <summary>
/// The colors that are shared by the whole interface (the "Appearance" page). A place that has a "Custom color" turned on uses its own color;
/// every other place uses the one stored here, so changing it here changes all of them at once.
/// </summary>
public sealed class GlobalColors
{
    public Vector4 Background { get; set; } = Theme.BarBackground;
    public Vector4 Border { get; set; } = Theme.BarBorder;
    public Vector4 Text { get; set; } = new(1f, 1f, 1f, 1f);
    public Vector4 Shield { get; set; } = DefaultShield;
    public Vector4 Mp { get; set; } = DefaultMp;

    /// <summary>HP colour of friendly characters that are not players (players use the colour of their role).</summary>
    public Vector4 FriendlyNpc { get; set; } = DefaultFriendlyNpc;

    public Vector4 EnemyHp { get; set; } = DefaultEnemyHp;

    /// <summary>HP colour of a pet or a minion.</summary>
    public Vector4 PetMinion { get; set; } = DefaultPetMinion;

    /// <summary>Colour of an object: furniture, chest, aetheryte, event object...</summary>
    public Vector4 ObjectColor { get; set; } = DefaultObject;

    /// <summary>Colour of the time left written over the icons (trackers excluded: they have their own texts).</summary>
    public Vector4 TimerText { get; set; } = DefaultText;

    /// <summary>Colour of the stack count written over the icons.</summary>
    public Vector4 StacksText { get; set; } = DefaultText;

    /// <summary>Corner rounding of every bar and frame that does not have its own, in pixels (0 = square corners).</summary>
    public int Rounding { get; set; } = DefaultRounding;

    public const int DefaultRounding = 6;
    public static readonly Vector4 DefaultShield = new(1f, 0.85f, 0.25f, 1f);
    public static readonly Vector4 DefaultMp = new(0.36f, 0.55f, 0.94f, 1f);
    public static readonly Vector4 DefaultText = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 DefaultFriendlyNpc = new(0.31f, 0.70f, 0.43f, 1f);
    public static readonly Vector4 DefaultEnemyHp = new(0.79f, 0.29f, 0.27f, 1f);
    public static readonly Vector4 DefaultPetMinion = new(0.50f, 0.62f, 0.78f, 1f);
    public static readonly Vector4 DefaultObject = new(0.55f, 0.57f, 0.62f, 1f);

    private static readonly GlobalColors Fallback = new();

    /// <summary>Where the colors in use are read from (the configuration of the plugin). Set once when the plugin starts.</summary>
    [JsonIgnore]
    public static Func<GlobalColors>? Source { get; set; }

    /// <summary>The colors in use right now.</summary>
    [JsonIgnore]
    public static GlobalColors Current => Source?.Invoke() ?? Fallback;

    /// <summary>True when two colors are the same one (a saved color can differ in the last decimals).</summary>
    public static bool Same(Vector4 a, Vector4 b) =>
        MathF.Abs(a.X - b.X) < 0.004f && MathF.Abs(a.Y - b.Y) < 0.004f && MathF.Abs(a.Z - b.Z) < 0.004f && MathF.Abs(a.W - b.W) < 0.004f;
}
