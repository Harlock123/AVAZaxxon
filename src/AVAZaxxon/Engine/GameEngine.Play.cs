namespace AVAZaxxon.Engine;

public sealed partial class GameEngine
{
    public const double SlideSpeed = 70, ClimbSpeed = 42;
    public const double ShotSpeed = 320, ShotRange = 250, FireDelay = 0.16;
    public const int MaxShots = 4;
    public const double FuelMax = 100, FuelDrain = 2.2, TankFuel = 24, LowFuel = 25;
    public const int FirstExtraLife = 10000, ExtraLifeEvery = 20000;
    public const double DyingSeconds = 2.6, ClearSeconds = 4;

    public int Level { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public double Fuel { get; private set; }
    public Section Section { get; private set; }
    /// <summary>How far along the camera is.</summary>
    public double Cam { get; private set; }
    public Ship Ship { get; } = new();
    public List<Target> Targets { get; } = [];
    public List<Barrier> Barriers { get; } = [];
    public List<Bolt> Shots { get; } = [];
    /// <summary>The guns' and the fighters' shells.</summary>
    public List<Bolt> Shells { get; } = [];
    /// <summary>Rockets rising from the silos.</summary>
    public List<Bolt> Rockets { get; } = [];
    public List<Fighter> Fighters { get; } = [];
    public Robot? Robot { get; private set; }
    /// <summary>The robot's homing missile, when one is in the air.</summary>
    public Bolt? Missile { get; private set; }
    /// <summary>Where the fortress deck runs (none in open space).</summary>
    public double DeckStart { get; private set; }
    public double DeckEnd { get; private set; }
    public bool HasDeck => DeckEnd > DeckStart;
    /// <summary>The fighters still to come or still in the sky.</summary>
    public int PlanesLeft { get; private set; }
    /// <summary>What brought the last ship down.</summary>
    public string Cause { get; private set; } = "";
    public MachinePilot Pilot { get; } = new();
    /// <summary>For tests: no guns fire and no rockets go up unless a test asks.</summary>
    internal bool Calm { get; set; }

    /// <summary>The forward speed, a little faster each level.</summary>
    public double Speed => Math.Min(78, 55 + 4 * (Level - 1));

    private int _nextExtra;
    private int _planesToSpawn;
    private double _spawnIn, _fireCooldown, _warnIn, _spaceDone;

    private void NewGame(int level)
    {
        _rng = new Random(_seed + level);
        Score = 0;
        Lives = 3;
        _nextExtra = FirstExtraLife;
        Cam = 0;
        StartLevel(level);
    }

    private void StartLevel(int level)
    {
        Level = level;
        Fuel = FuelMax;
        Ship.Y = 0;
        Ship.Z = 24;
        Ship.X = Cam + Field.ShipAhead;
        Effects.RemoveAll(e => e.Kind != EffectKind.BigBoom);
        Enter(Section.Fortress);
        SetPhase(Phase.Flying);
    }

    /// <summary>Clears the sky and lays out a section, starting just over the horizon.</summary>
    private void Enter(Section s)
    {
        Section = s;
        // The robot stands at the end of the inner fortress, so what is behind the ship stays in sight.
        if (s != Section.Robot)
        {
            Targets.Clear();
            Barriers.Clear();
        }
        Shots.Clear();
        Shells.Clear();
        Rockets.Clear();
        Fighters.Clear();
        Missile = null;
        Robot = null;
        var rng = new Random(Level * 97 + (int)s * 13 + 5);
        switch (s)
        {
            case Section.Fortress:
            case Section.Inner:
                double length = s == Section.Fortress ? 2000 + 200 * Math.Min(Level, 4) : 1800 + 150 * Math.Min(Level, 4);
                DeckStart = Cam + Field.Horizon - 20;
                DeckEnd = DeckStart + length;
                LayOut(rng, s == Section.Inner);
                if (State is GameState.Playing or GameState.Attract)
                    Say(s == Section.Fortress ? "THE FORTRESS" : "THE INNER FORTRESS", s == Section.Fortress ? $"LEVEL {Level}" : "SHOOT THE FUEL TANKS");
                break;
            case Section.Space:
                DeckStart = DeckEnd = 0;
                _planesToSpawn = PlanesLeft = Math.Min(24, 12 + 2 * Level);
                _spawnIn = 2.2;
                _spaceDone = 0;
                if (State is GameState.Playing or GameState.Attract) Say("OPEN SPACE", $"{PlanesLeft} ENEMY FIGHTERS");
                break;
            case Section.Robot:
                // The deck runs on under the robot, as far as it needs to.
                DeckStart = Math.Min(DeckStart, Cam - 200);
                DeckEnd = double.MaxValue / 4;
                Robot = new Robot { X = Cam + Field.Horizon + 20, Y = 0 };
                if (State is GameState.Playing or GameState.Attract) Say("THE GOLIATH", "HIT ITS LAUNCHER BEFORE IT FIRES");
                break;
        }
        if (State is GameState.Playing or GameState.Attract) Play(Sfx.Section);
    }

    /// <summary>
    /// Lays out a fortress deck: a wall at the gate, then groups of guns, fuel tanks, radar dishes,
    /// parked planes and rocket silos, broken by more walls and force fields.
    /// </summary>
    private void LayOut(Random rng, bool inner)
    {
        AddWall(DeckStart + 70, rng, wide: true);
        double x = DeckStart + 220, lastBarrier = DeckStart + 70;
        double end = inner ? DeckEnd - 420 : DeckEnd - 240;
        int group = 0;
        while (x < end)
        {
            if (x - lastBarrier > (inner ? 230 : 280) && rng.Next(100) < (inner ? 45 : 32))
            {
                if (rng.Next(100) < 50) AddWall(x, rng, wide: false);
                else AddField(x, rng);
                lastBarrier = x;
                x += 110;
                continue;
            }
            int count = 2 + rng.Next(inner ? 3 : 2) + (Level >= 3 ? 1 : 0);
            var ys = new List<double>();
            for (int i = 0; i < count; i++)
            {
                double y = 0;
                for (int tries = 0; tries < 12; tries++)
                {
                    y = -44 + rng.Next(89);
                    if (ys.All(o => Math.Abs(o - y) > 18)) break;
                }
                ys.Add(y);
                TargetKind kind = i == 0 && group % 3 == 1 ? TargetKind.FuelTank : rng.Next(100) switch
                {
                    < 30 => TargetKind.Turret,
                    < 45 => TargetKind.FuelTank,
                    < 53 => TargetKind.Radar,
                    < 75 => TargetKind.Plane,
                    _ => TargetKind.Silo,
                };
                Targets.Add(new Target { Kind = kind, X = x + rng.Next(50), Y = y, Cooldown = 0.8 + rng.NextDouble() * 1.5 });
            }
            group++;
            x += 100 + rng.Next(50);
        }
        if (!inner) AddWall(DeckEnd - 90, rng, wide: false);
    }

    private void AddWall(double x, Random rng, bool wide)
    {
        double half = wide ? 22 : Math.Max(13, 19 - Level);
        double centre = -30 + rng.Next(61);
        Barriers.Add(new Barrier
        {
            X = x, GapLo = centre - half, GapHi = centre + half,
            GapTop = wide ? 32 : 18 + rng.Next(14),
        });
    }

    private void AddField(double x, Random rng) =>
        Barriers.Add(new Barrier { X = x, IsField = true, FieldLow = 13 + rng.Next(14) });

    /// <summary>The title's backdrop: the camera flies on over endless fortress.</summary>
    private void Drift(double dt)
    {
        Cam += Speed * dt;
        Ship.X = Cam + Field.ShipAhead;
        if (Section is not (Section.Fortress or Section.Inner) || Cam > DeckEnd) Enter(Section.Fortress);
        Tidy();
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        Cam += Speed * dt;
        switch (Phase)
        {
            case Phase.Flying:
                Fly(dt, c);
                break;
            case Phase.Dying:
                Ship.X = Cam + Field.ShipAhead;
                MoveThings(dt, false);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.LevelClear:
                Ship.X = Cam + Field.ShipAhead;
                Steer(dt, c);
                MoveThings(dt, false);
                if (PhaseTime > ClearSeconds) StartLevel(Level + 1);
                break;
        }
        Tidy();
    }

    private void Steer(double dt, Controls c)
    {
        Ship.Dx = c.Dx;
        Ship.Dy = c.Dy;
        Ship.Y = Math.Clamp(Ship.Y + c.Dx * SlideSpeed * dt, -Field.MaxY, Field.MaxY);
        Ship.Z = Math.Clamp(Ship.Z + c.Dy * ClimbSpeed * dt, Field.MinZ, Field.MaxZ);
    }

    private void Fly(double dt, Controls c)
    {
        Ship.X = Cam + Field.ShipAhead;
        Steer(dt, c);

        // Firing: hold the button for a stream.
        _fireCooldown -= dt;
        if (c.Fire && _fireCooldown <= 0 && Shots.Count < MaxShots)
        {
            _fireCooldown = FireDelay;
            Shots.Add(new Bolt { X = Ship.X + 8, Y = Ship.Y, Z = Ship.Z, Vx = Speed + ShotSpeed, Life = ShotRange / ShotSpeed });
            Play(Sfx.Fire);
        }

        // Fuel burns over the fortress.
        if (Section is Section.Fortress or Section.Inner)
        {
            Fuel = Math.Max(0, Fuel - FuelDrain * dt);
            if (Fuel < LowFuel)
            {
                _warnIn -= dt;
                if (_warnIn <= 0) { _warnIn = 0.9; Play(Sfx.Warning); }
            }
            if (Fuel <= 0) { Die("OUT OF FUEL"); return; }
        }

        MoveThings(dt, true);
        if (Phase != Phase.Flying) return;

        // Walls and force fields.
        foreach (var b in Barriers)
        {
            if (Ship.X + 6 < b.X || Ship.X - 6 > b.X + Barrier.Thickness) continue;
            if (Corners(Ship.Y, Ship.Z).Any(p => b.Blocks(p.Y, p.Z)))
            {
                Die(b.IsField ? "FORCE FIELD" : "HIT THE WALL");
                return;
            }
        }
        // Flying into something on the deck.
        foreach (var t in Targets.Where(t => !t.Dead))
        {
            if (Math.Abs(t.X - Ship.X) < t.HalfWidth + 5 && Math.Abs(t.Y - Ship.Y) < t.HalfWidth + 4 && Ship.Z - 2.5 < t.Height)
            {
                Destroy(t, false);
                Die("CRASHED");
                return;
            }
        }
        Sections(dt);
    }

    private static IEnumerable<(double Y, double Z)> Corners(double y, double z)
    {
        yield return (y - 4, z - 2.5);
        yield return (y + 4, z - 2.5);
        yield return (y - 4, z + 2.5);
        yield return (y + 4, z + 2.5);
    }

    /// <summary>On from one section to the next.</summary>
    private void Sections(double dt)
    {
        switch (Section)
        {
            case Section.Fortress when Ship.X > DeckEnd + 160:
                Enter(Section.Space);
                break;
            case Section.Space:
                if (_planesToSpawn == 0 && Fighters.Count == 0)
                {
                    _spaceDone += dt;
                    if (_spaceDone > 1.5) Enter(Section.Inner);
                }
                break;
            case Section.Inner when Ship.X > DeckEnd - 330:
                Enter(Section.Robot);
                break;
        }
    }

    /// <summary>Shots, shells, rockets, guns, fighters and the robot.</summary>
    private void MoveThings(double dt, bool live)
    {
        var ship = Ship;
        // The player's shots.
        foreach (var s in Shots)
        {
            double x0 = s.X;
            Advance(s, dt);
            if (s.Dead) continue;
            foreach (var b in Barriers)
                if (x0 < b.X + Barrier.Thickness && s.X > b.X && b.Blocks(s.Y, s.Z))
                {
                    s.Dead = true;
                    Spark(b.X, s.Y, s.Z);
                    break;
                }
            if (s.Dead) continue;
            foreach (var t in Targets)
            {
                if (t.Dead || Math.Abs(t.X - s.X) > t.HalfWidth + 4 || Math.Abs(t.Y - s.Y) > t.HalfWidth + 2 || s.Z > t.Height + 2) continue;
                s.Dead = true;
                Destroy(t, true);
                break;
            }
            if (s.Dead) continue;
            foreach (var r in Rockets)
            {
                if (r.Dead || Math.Abs(r.X - s.X) > 7 || Math.Abs(r.Y - s.Y) > 5 || Math.Abs(r.Z + 3 - s.Z) > 7) continue;
                s.Dead = r.Dead = true;
                Boom(r.X, r.Y, r.Z, false);
                AddScore(200, r.X, r.Y, r.Z);
                Play(Sfx.Hit);
                break;
            }
            if (s.Dead) continue;
            foreach (var f in Fighters)
            {
                if (f.Dead || Math.Abs(f.X - s.X) > 8 || Math.Abs(f.Y - s.Y) > 6 || Math.Abs(f.Z - s.Z) > 5) continue;
                s.Dead = f.Dead = true;
                PlanesLeft--;
                Boom(f.X, f.Y, f.Z, false);
                AddScore(150, f.X, f.Y, f.Z);
                Play(Sfx.Hit);
                break;
            }
            if (s.Dead) continue;
            if (Missile is { } m && Math.Abs(m.X - s.X) < 9 && Math.Abs(m.Y - s.Y) < 5 && Math.Abs(m.Z - s.Z) < 5)
            {
                s.Dead = true;
                Missile = null;
                Boom(m.X, m.Y, m.Z, false);
                AddScore(500, m.X, m.Y, m.Z);
                Play(Sfx.Hit);
                continue;
            }
            if (Robot is { Dead: false } robot) ShootRobot(robot, s);
        }

        // The guns.
        foreach (var t in Targets)
        {
            if (t.Dead) continue;
            double ahead = t.X - ship.X;
            if (t.Kind == TargetKind.Turret && live && !Calm)
            {
                t.Cooldown -= dt;
                if (t.Cooldown <= 0 && ahead is > 50 and < 250)
                {
                    t.Cooldown = Math.Max(1.1, 2.3 - 0.15 * Level) + _rng.NextDouble() * 0.8;
                    Shells.Add(Aim(t.X, t.Y, t.Height, 95 + 8 * Math.Min(Level, 6)));
                    Play(Sfx.EnemyFire);
                }
            }
            if (t.Kind == TargetKind.Silo && !t.Launched && live && !Calm && ahead is > 40 and < 150 && Math.Abs(t.Y - ship.Y) < 30)
            {
                t.Launched = true;
                Rockets.Add(new Bolt { X = t.X, Y = t.Y, Z = 3, Vx = 0, Vz = 30 + 2 * Level, Life = 3 });
                Play(Sfx.Launch);
            }
        }
        foreach (var r in Rockets) Advance(r, dt);
        foreach (var sh in Shells)
        {
            Advance(sh, dt);
            if (sh.Z < 0) sh.Dead = true;
            foreach (var b in Barriers)
                if (Math.Abs(sh.X - (b.X + 2)) < 3 && b.Blocks(sh.Y, sh.Z)) sh.Dead = true;
        }

        if (Section == Section.Space) Squadron(dt, live);
        if (Robot is { } rb) MoveRobot(rb, dt, live);
        if (Missile is { } mi)
        {
            // It turns towards the ship, but not quickly.
            double turn = 20 + 2 * Math.Min(Level, 6);
            mi.Vy = Math.Clamp(ship.Y - mi.Y, -1, 1) * turn;
            mi.Vz = Math.Clamp(ship.Z - mi.Z, -1, 1) * turn * 0.7;
            Advance(mi, dt);
            if (mi.X < Cam - 30) Missile = null;
        }

        if (!live) return;
        // What hits the ship.
        foreach (var sh in Shells)
            if (!sh.Dead && Near(sh.X, sh.Y, sh.Z, 5, 4.5, 3.5)) { sh.Dead = true; Die("SHOT DOWN"); return; }
        foreach (var r in Rockets)
            if (!r.Dead && Math.Abs(r.X - ship.X) < 6 && Math.Abs(r.Y - ship.Y) < 5 && ship.Z > r.Z - 3 && ship.Z < r.Z + 9) { r.Dead = true; Die("HIT BY A ROCKET"); return; }
        foreach (var f in Fighters)
            if (!f.Dead && Near(f.X, f.Y, f.Z, 7, 7, 4))
            {
                f.Dead = true;
                PlanesLeft--;
                Boom(f.X, f.Y, f.Z, false);
                Die("COLLISION");
                return;
            }
        if (Missile is { } hm && Near(hm.X, hm.Y, hm.Z, 7, 5, 4)) { Missile = null; Die("HOMING MISSILE"); return; }
    }

    private bool Near(double x, double y, double z, double rx, double ry, double rz) =>
        Math.Abs(x - Ship.X) < rx && Math.Abs(y - Ship.Y) < ry && Math.Abs(z - Ship.Z) < rz;

    private static void Advance(Bolt b, double dt)
    {
        b.X += b.Vx * dt;
        b.Y += b.Vy * dt;
        b.Z += b.Vz * dt;
        b.Age += dt;
        if (b.Age > b.Life) b.Dead = true;
    }

    /// <summary>A shell from (x, y, z) aimed where the ship will be when it gets there.</summary>
    private Bolt Aim(double x, double y, double z, double speed)
    {
        double tx = Ship.X, ty = Ship.Y, tz = Ship.Z, time = 0;
        for (int i = 0; i < 3; i++)
        {
            double d = Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y) + (tz - z) * (tz - z));
            time = d / speed;
            tx = Ship.X + Speed * time;
        }
        double k = 1 / Math.Max(0.05, time);
        return new Bolt { X = x, Y = y, Z = z, Vx = (tx - x) * k, Vy = (ty - y) * k, Vz = (tz - z) * k, Life = 3 };
    }

    // ───────────────────────────── open space ─────────────────────────────

    private void Squadron(double dt, bool live)
    {
        if (live && _planesToSpawn > 0)
        {
            _spawnIn -= dt;
            if (_spawnIn <= 0)
            {
                _spawnIn = Math.Max(0.6, 1.25 - 0.08 * Level) + _rng.NextDouble() * 0.5;
                int n = _planesToSpawn >= 2 && _rng.Next(3) == 0 ? 2 : 1;
                double y = -40 + _rng.Next(81), z = 8 + _rng.Next(44);
                for (int i = 0; i < n; i++)
                {
                    Fighters.Add(new Fighter
                    {
                        X = Cam + Field.Horizon + 10 + i * 30, Y = Math.Clamp(y + i * 16, -Field.MaxY, Field.MaxY), Z = z,
                        Vx = Speed - (45 + 4 * Math.Min(Level, 6)),
                        FireIn = 0.6 + _rng.NextDouble() * 1.2, Wobble = _rng.NextDouble() * 6,
                    });
                    _planesToSpawn--;
                }
            }
        }
        foreach (var f in Fighters)
        {
            if (f.Dead) continue;
            double ahead = f.X - Ship.X;
            f.X += f.Vx * dt;
            f.Wobble += dt;
            if (ahead > 70)
            {
                double edge = 11 + Level;
                f.Y = Math.Clamp(f.Y + Math.Clamp(Ship.Y - f.Y, -1, 1) * edge * dt + Math.Sin(f.Wobble * 2.1) * 6 * dt, -Field.MaxY, Field.MaxY);
                f.Z = Math.Clamp(f.Z + Math.Clamp(Ship.Z - f.Z, -1, 1) * edge * 0.6 * dt, Field.MinZ, Field.MaxZ);
            }
            if (live && !Calm)
            {
                f.FireIn -= dt;
                if (f.FireIn <= 0 && ahead is > 80 and < 230)
                {
                    f.FireIn = Math.Max(1.2, 2.6 - 0.15 * Level) + _rng.NextDouble();
                    Shells.Add(Aim(f.X - 6, f.Y, f.Z, 110 + 6 * Math.Min(Level, 6)));
                    Play(Sfx.EnemyFire);
                }
            }
            if (f.X < Cam - 40)
            {
                f.Dead = true;
                PlanesLeft--;
            }
        }
    }

    // ───────────────────────────── the robot ─────────────────────────────

    /// <summary>How long the robot takes to charge its missile.</summary>
    public double ChargeTime => Math.Max(5, 10 - 0.8 * (Level - 1));

    private void MoveRobot(Robot r, double dt, bool live)
    {
        r.Age += dt;
        r.Flash = Math.Max(0, r.Flash - dt);
        if (r.Dead)
        {
            r.DeadTime += dt;
            r.X = Math.Max(r.X, Ship.X + 170);
            return;
        }
        // It walks backwards ahead of the ship, swaying from side to side.
        r.X = Math.Max(r.X, Ship.X + 170);
        r.Y = Math.Sin(r.Age * 0.55) * 24;
        if (!live || Missile != null || Phase != Phase.Flying || r.X > Ship.X + 175) return;
        double before = r.Charge;
        r.Charge += dt;
        if (before < ChargeTime * 0.6 && r.Charge >= ChargeTime * 0.6) Play(Sfx.Alarm);
        if (r.Charge >= ChargeTime)
        {
            r.Charge = 0;
            var (lx, ly, lz) = r.Launcher;
            Missile = new Bolt { X = lx - 4, Y = ly, Z = lz, Vx = Speed - 62 - 3 * Math.Min(Level, 6), Life = 12 };
            Play(Sfx.Homing);
        }
    }

    private void ShootRobot(Robot r, Bolt s)
    {
        var (lx, ly, lz) = r.Launcher;
        if (Math.Abs(s.X - lx) < 8 && Math.Abs(s.Y - ly) < 6 && Math.Abs(s.Z - lz) < 6)
        {
            s.Dead = true;
            r.Hits++;
            r.Flash = 0.25;
            AddScore(200, lx, ly, lz);
            if (r.Hits >= Robot.HitsToKill)
            {
                r.Dead = true;
                Missile = null;
                Boom(r.X, r.Y, 30, true);
                Boom(lx, ly, lz, true);
                AddScore(3000, r.X, r.Y, 50);
                Play(Sfx.BigBoom);
                Play(Sfx.LevelClear);
                Shells.Clear();
                SetPhase(Phase.LevelClear);
                Say("THE GOLIATH IS DOWN!", $"LEVEL {Level} CLEARED", ClearSeconds);
            }
            else Play(Sfx.Hit);
            return;
        }
        if (s.X > r.X - 6 && Math.Abs(s.Y - r.Y) < 18 && s.Z < 50)
        {
            s.Dead = true;
            Spark(r.X - 6, s.Y, s.Z);
            Play(Sfx.Clank);
        }
    }

    // ───────────────────────────── scoring and losing ─────────────────────────────

    private void Destroy(Target t, bool shot)
    {
        t.Dead = true;
        bool big = t.Kind is TargetKind.Radar or TargetKind.FuelTank;
        Boom(t.X, t.Y, t.Height / 2, big);
        if (!shot) return;
        AddScore(t.Points, t.X, t.Y, t.Height + 4);
        Play(big ? Sfx.Boom : Sfx.Hit);
        if (t.Kind == TargetKind.FuelTank)
        {
            Fuel = Math.Min(FuelMax, Fuel + TankFuel);
            Play(Sfx.Refuel);
        }
    }

    private void AddScore(int points, double x, double y, double z)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Z = z, Text = points.ToString(), Life = 1, Colour = points >= 1000 ? 0xFFFFE14A : 0xFFF2F2F2 });
        if (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
            Say("EXTRA SHIP!", null, 1.5);
        }
    }

    private void Boom(double x, double y, double z, bool big) =>
        Effects.Add(new Effect { Kind = big ? EffectKind.BigBoom : EffectKind.Boom, X = x, Y = y, Z = z, Life = big ? 1.4 : 0.8 });

    private void Spark(double x, double y, double z) =>
        Effects.Add(new Effect { Kind = EffectKind.Spark, X = x, Y = y, Z = z, Life = 0.3 });

    private void Die(string cause)
    {
        Cause = cause;
        Lives--;
        Boom(Ship.X, Ship.Y, Ship.Z, true);
        Play(Sfx.PlayerBoom);
        Shots.Clear();
        SetPhase(Phase.Dying);
        Say(cause, Lives > 0 ? $"{Lives} {(Lives == 1 ? "SHIP" : "SHIPS")} LEFT" : null, DyingSeconds);
    }

    /// <summary>A new ship, at the start of the section it was lost in.</summary>
    private void Respawn()
    {
        Fuel = FuelMax;
        Ship.Y = 0;
        Ship.Z = 24;
        Ship.X = Cam + Field.ShipAhead;
        var s = Section;
        // The robot is met again at the end of the inner fortress.
        Enter(s == Section.Robot ? Section.Inner : s);
        SetPhase(Phase.Flying);
    }

    /// <summary>Drops what has gone behind the camera.</summary>
    private void Tidy()
    {
        double behind = Cam - 120;
        Targets.RemoveAll(t => t.X < behind);
        Barriers.RemoveAll(b => b.X < behind);
        Shots.RemoveAll(s => s.Dead);
        Shells.RemoveAll(s => s.Dead || s.X < behind);
        Rockets.RemoveAll(r => r.Dead || r.X < behind);
        Fighters.RemoveAll(f => f.Dead);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Starts at a level and a section, with the sky clear.</summary>
    internal void StartAt(int level, Section section = Section.Fortress)
    {
        StartLevel(level);
        if (section != Section.Fortress) Enter(section);
        Message = SubMessage = null;
    }

    /// <summary>An empty deck under the ship, from just behind it to well ahead.</summary>
    internal void Clear(bool deck = true)
    {
        Targets.Clear();
        Barriers.Clear();
        Shells.Clear();
        Rockets.Clear();
        Fighters.Clear();
        Robot = null;
        Missile = null;
        if (deck)
        {
            DeckStart = Cam - 200;
            DeckEnd = Cam + 5000;
        }
    }

    internal void Put(double y, double z)
    {
        Ship.Y = y;
        Ship.Z = z;
    }

    internal Target Place(TargetKind kind, double ahead, double y)
    {
        var t = new Target { Kind = kind, X = Ship.X + ahead, Y = y, Cooldown = 0 };
        Targets.Add(t);
        return t;
    }

    internal Barrier Wall(double ahead, double gapLo, double gapHi, double gapTop)
    {
        var b = new Barrier { X = Ship.X + ahead, GapLo = gapLo, GapHi = gapHi, GapTop = gapTop };
        Barriers.Add(b);
        return b;
    }

    internal Barrier ForceField(double ahead, double low)
    {
        var b = new Barrier { X = Ship.X + ahead, IsField = true, FieldLow = low };
        Barriers.Add(b);
        return b;
    }

    internal Fighter AddFighter(double ahead, double y, double z)
    {
        var f = new Fighter { X = Ship.X + ahead, Y = y, Z = z, Vx = Speed - 50, FireIn = 99 };
        Fighters.Add(f);
        return f;
    }

    internal Robot MeetRobot()
    {
        Enter(Section.Robot);
        Robot!.X = Ship.X + 170;
        Message = SubMessage = null;
        return Robot;
    }

    internal void SetFuel(double f) => Fuel = f;
    internal void SetScore(int s) => Score = s;
    internal void SetLives(int n) => Lives = n;
    internal void EndSpaceSpawning() => _planesToSpawn = 0;
}
