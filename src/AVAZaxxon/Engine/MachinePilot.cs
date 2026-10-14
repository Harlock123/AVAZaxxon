namespace AVAZaxxon.Engine;

/// <summary>
/// The computer at the controls, for the demonstration (and the tests). It lines up with the hole
/// in the next wall, ducks under force fields, hunts fuel tanks when the gauge runs low and guns
/// otherwise, dodges shells, and in open space and against the robot lines up with what it wants
/// to hit. It holds the trigger down.
/// </summary>
public sealed class MachinePilot
{
    /// <summary>Where it is trying to put the ship, for the tests and for tuning.</summary>
    public double WantY { get; private set; }
    public double WantZ { get; private set; }

    private (double Y, double Z)? _escape;

    public Controls Decide(GameEngine g)
    {
        var s = g.Ship;
        double wantY = s.Y, wantZ = 6;
        double cruise = 6;

        // The next wall or field ahead decides the height, and a wall the line too.
        var next = g.Barriers.Where(b => b.X + Barrier.Thickness + 6 > s.X && b.X - s.X < 230).OrderBy(b => b.X).FirstOrDefault();
        bool lined = false;
        if (next != null)
        {
            if (next.IsField) cruise = Math.Min(cruise, next.FieldLow - 6);
            else
            {
                double centre = (next.GapLo + next.GapHi) / 2;
                wantY = Math.Clamp(centre, next.GapLo + 6, next.GapHi - 6);
                cruise = Math.Min(cruise, next.GapTop - 6);
                lined = true;
            }
        }
        wantZ = cruise;

        switch (g.Section)
        {
            case Section.Fortress or Section.Inner:
            {
                if (!lined)
                {
                    // Fuel first when it runs low; otherwise anything worth shooting, guns first.
                    bool hungry = g.Fuel < 60;
                    var target = g.Targets
                        .Where(t => !t.Dead && t.X - s.X is > 35 and < 260)
                        .OrderBy(t => (hungry && t.Kind == TargetKind.FuelTank ? -400 : 0) + (t.Kind == TargetKind.Turret ? -60 : 0) + (t.X - s.X) + Math.Abs(t.Y - s.Y) * 1.5)
                        .FirstOrDefault();
                    if (target != null) wantY = target.Y;
                }
                // Don't fly into things on the deck: hop over anything close and not shot yet.
                // Anywhere between here and where it is heading across counts.
                double lo = Math.Min(s.Y, wantY), hi = Math.Max(s.Y, wantY);
                foreach (var t in g.Targets)
                    if (!t.Dead && t.X - s.X is > -10 and < 34 && t.Y + t.HalfWidth + 6 > lo && t.Y - t.HalfWidth - 6 < hi)
                        wantZ = Math.Max(wantZ, t.Height + 5);
                // Rockets: keep out of their column.
                foreach (var r in g.Rockets)
                    if (!r.Dead && r.X - s.X is > -6 and < 60 && Math.Abs(r.Y - s.Y) < 10)
                        wantY = s.Y + (r.Y > s.Y ? -16 : 16);
                break;
            }
            case Section.Space:
            {
                var f = g.Fighters.Where(f => !f.Dead && f.X - s.X > 20).OrderBy(f => f.X - s.X).FirstOrDefault();
                if (f != null)
                {
                    wantY = f.Y;
                    wantZ = f.Z;
                }
                else wantZ = 24;
                break;
            }
            case Section.Robot when g.Robot is { Dead: false } robot:
            {
                if (g.Missile is { } m)
                {
                    wantY = m.Y;
                    wantZ = m.Z;
                }
                else
                {
                    var (_, ly, lz) = robot.Launcher;
                    wantY = ly;
                    wantZ = lz;
                }
                break;
            }
        }

        // Shells that will pass close: try a few ways out and take the one that keeps furthest from
        // them without flying into the next wall.
        var threats = new List<(double Y, double Z)>();
        foreach (var sh in g.Shells)
        {
            double rx = sh.X - s.X, ry = sh.Y - s.Y, rz = sh.Z - s.Z;
            double vx = sh.Vx - g.Speed, vy = sh.Vy, vz = sh.Vz;
            double v2 = vx * vx + vy * vy + vz * vz;
            double t = v2 < 1 ? 0 : Math.Clamp(-(rx * vx + ry * vy + rz * vz) / v2, 0, 1.0);
            double cx = rx + vx * t, cy = ry + vy * t, cz = rz + vz * t;
            if (Math.Abs(cx) < 10 && Math.Abs(cy) < 12 && Math.Abs(cz) < 9) threats.Add((s.Y + cy, s.Z + cz));
        }
        bool Fits(double y, double z) =>
            Math.Abs(y) <= Field.MaxY && !(next is { } w && w.X - s.X < 70 && (w.Blocks(y - 5, z) || w.Blocks(y + 5, z) || w.Blocks(y, z + 4)));
        double Clear(double y, double z) => threats.Min(p => Math.Max(Math.Abs(p.Y - y) / 6, Math.Abs(p.Z - z) / 4.5));
        if (threats.Count == 0) _escape = null;
        // Once it has picked a way out it sticks to it while it still works, rather than dithering.
        else if (_escape is { } e && Fits(e.Y, e.Z) && Clear(e.Y, e.Z) >= 1.3) (wantY, wantZ) = e;
        else
        {
            (double Y, double Z) best = (wantY, wantZ);
            double bestScore = double.MinValue;
            foreach (var (oy, oz) in new[] { (0.0, 0.0), (16, 0), (-16, 0), (0, 12), (0, -12), (12, 9), (-12, 9), (12, -9), (-12, -9), (24, 0), (-24, 0) })
            {
                double y = s.Y + oy, z = Math.Clamp(s.Z + oz, Field.MinZ + 1, Field.MaxZ - 1);
                if (!Fits(y, z)) continue;
                double clear = Clear(y, z);
                double score = Math.Min(clear, 2.5) - (Math.Abs(oy) + Math.Abs(oz)) * 0.01;
                if (score > bestScore) { bestScore = score; best = (y, z); }
            }
            (wantY, wantZ) = best;
            _escape = best;
        }

        wantZ = Math.Clamp(wantZ, Field.MinZ + 1, Field.MaxZ - 1);
        wantY = Math.Clamp(wantY, -Field.MaxY, Field.MaxY);
        WantY = wantY;
        WantZ = wantZ;
        int dx = Math.Abs(wantY - s.Y) < 1.3 ? 0 : Math.Sign(wantY - s.Y);
        int dh = Math.Abs(wantZ - s.Z) < 0.8 ? 0 : Math.Sign(wantZ - s.Z);
        return new Controls(dx, dh, true);
    }
}
