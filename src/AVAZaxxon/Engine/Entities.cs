namespace AVAZaxxon.Engine;

/// <summary>Left and right slide the ship across the deck, up and down climb and dive; the button fires.</summary>
public readonly record struct Controls(int Dx, int Dy, bool Fire = false);

/// <summary>
/// The world: X runs forward along the flight (up and to the right on screen), Y across the deck
/// (down and to the right), and Z is height above the deck. The ship flies forward at a steady
/// speed with the camera; everything else stands still or flies on its own.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The bottom of the score bar.</summary>
    public const double Top = 64;
    /// <summary>Half the width of the fortress deck.</summary>
    public const double Deck = 56;
    /// <summary>How far across, and how high, the ship can go.</summary>
    public const double MaxY = 50, MinZ = 3, MaxZ = 56;
    /// <summary>The height of the walls and the force fields' pylons: higher than the ship can climb.</summary>
    public const double Ceiling = 64;
    /// <summary>How far ahead of the camera the ship flies.</summary>
    public const double ShipAhead = 70;
    /// <summary>How far ahead of the camera things come into view.</summary>
    public const double Horizon = 340;
}

/// <summary>Which part of a level the ship is flying through.</summary>
public enum Section
{
    /// <summary>The outer fortress: walls, force fields, guns, fuel.</summary>
    Fortress,
    /// <summary>Open space, against a squadron of fighters.</summary>
    Space,
    /// <summary>The inner fortress, closer packed.</summary>
    Inner,
    /// <summary>The robot at the heart of the fortress.</summary>
    Robot,
}

public sealed class Ship
{
    public double X, Y, Z = 20;
    /// <summary>How fast it is sliding and climbing, for the bank and the pitch drawn.</summary>
    public int Dx, Dy;
}

public enum TargetKind { Turret, FuelTank, Radar, Plane, Silo }

/// <summary>Something standing on the deck, to shoot (or fly into).</summary>
public sealed class Target
{
    public TargetKind Kind;
    public double X, Y;
    public bool Dead;
    /// <summary>A gun's time to its next shot.</summary>
    public double Cooldown;
    /// <summary>A silo whose rocket has gone up.</summary>
    public bool Launched;

    public double HalfWidth => Kind switch
    {
        TargetKind.Turret => 5, TargetKind.FuelTank => 6, TargetKind.Radar => 6, TargetKind.Plane => 7, _ => 4,
    };

    public double Height => Kind switch
    {
        TargetKind.Turret => 9, TargetKind.FuelTank => 12, TargetKind.Radar => 14, TargetKind.Plane => 5, _ => 3,
    };

    public int Points => Kind switch
    {
        TargetKind.Turret => 200, TargetKind.FuelTank => 300, TargetKind.Radar => 1000, TargetKind.Plane => 50, _ => 150,
    };
}

/// <summary>
/// A wall across the deck with a hole in it to fly through, or a force field between two pylons
/// to fly under. Either way it is <see cref="Thickness"/> deep, starting at X.
/// </summary>
public sealed class Barrier
{
    public const double Thickness = 4;
    public double X;
    public bool IsField;
    /// <summary>A wall's hole: from GapLo to GapHi across, and from the deck up to GapTop.</summary>
    public double GapLo, GapHi, GapTop;
    /// <summary>A field's bottom edge: it fills everything above.</summary>
    public double FieldLow;

    /// <summary>Whether the point across and up is solid.</summary>
    public bool Blocks(double y, double z) =>
        IsField ? z > FieldLow && Math.Abs(y) < Field.Deck : Math.Abs(y) < Field.Deck && (y < GapLo || y > GapHi || z > GapTop);
}

/// <summary>A shot, a shell or a rocket: anything that flies in a straight line.</summary>
public sealed class Bolt
{
    public double X, Y, Z, Vx, Vy, Vz, Age, Life = 3;
    public bool Dead;
}

/// <summary>An enemy fighter in open space: it flies at the ship, edging towards its height and line, and shoots.</summary>
public sealed class Fighter
{
    public double X, Y, Z, Vx, FireIn, Wobble;
    public bool Dead;
}

/// <summary>The robot that guards the heart of the fortress, with a missile launcher on its arm.</summary>
public sealed class Robot
{
    public const int HitsToKill = 6;
    public double X, Y, Age;
    public int Hits;
    /// <summary>How long the missile has been charging.</summary>
    public double Charge;
    /// <summary>Flashes when hit.</summary>
    public double Flash;
    public bool Dead;
    public double DeadTime;

    /// <summary>Where the launcher on its arm is.</summary>
    public (double X, double Y, double Z) Launcher => (X - 8, Y + 14, 26);
}

public enum EffectKind { Boom, BigBoom, Score, Spark, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Z, Age, Life = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
