using AVAZaxxon.Engine;
using Engine = AVAZaxxon.Engine;

namespace AVAZaxxon.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avazaxxon-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in progress on an empty deck: no guns firing unless a test lets them.</summary>
    private static GameEngine Game(bool deck = true)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        g.StartAt(1);
        g.Clear(deck);
        return g;
    }

    private static Engine.Controls Stick(int dx = 0, int dy = 0, bool fire = false) => new(dx, dy, fire);

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_ship_flies_forward_by_itself_and_slides_climbs_and_dives_within_limits()
    {
        var g = Game();
        g.Put(0, 20);
        double x = g.Ship.X;
        g.Idle(60);
        Assert.Equal(g.Speed, g.Ship.X - x, 0);
        g.Run(30, Stick(dx: 1));
        Assert.Equal(GameEngine.SlideSpeed / 2, g.Ship.Y, 0);
        g.Run(30, Stick(dy: 1));
        Assert.Equal(20 + GameEngine.ClimbSpeed / 2, g.Ship.Z, 0);
        g.Run(300, Stick(dx: 1, dy: 1));
        Assert.Equal(Field.MaxY, g.Ship.Y);
        Assert.Equal(Field.MaxZ, g.Ship.Z);
        g.Run(300, Stick(dx: -1, dy: -1));
        Assert.Equal(-Field.MaxY, g.Ship.Y);
        Assert.Equal(Field.MinZ, g.Ship.Z);
    }

    [Fact]
    public void Holding_the_trigger_fires_a_stream_of_shots_but_only_a_few_at_once()
    {
        var g = Game();
        g.Run(1, Stick(fire: true));
        Assert.Single(g.Shots);
        g.Run(120, Stick(fire: true));
        Assert.InRange(g.Shots.Count, 2, GameEngine.MaxShots);
        g.Idle(60);
        Assert.Empty(g.Shots);
    }

    // ───────────────────────────── the deck ─────────────────────────────

    [Fact]
    public void A_fuel_tank_shot_low_down_scores_and_tops_up_the_fuel()
    {
        var g = Game();
        g.SetFuel(40);
        g.Put(10, 6);
        var tank = g.Place(TargetKind.FuelTank, 150, 10);
        g.Run(10, Stick(fire: true));
        g.Idle(60);
        Assert.True(tank.Dead);
        Assert.Equal(300, g.Score);
        Assert.InRange(g.Fuel, 40 + GameEngine.TankFuel - 3, 40 + GameEngine.TankFuel);
    }

    [Fact]
    public void Shots_fired_too_high_pass_over_what_stands_on_the_deck()
    {
        var g = Game();
        g.Put(-20, 30);
        var gun = g.Place(TargetKind.Turret, 120, -20);
        g.Run(10, Stick(fire: true));
        g.Idle(40);
        Assert.False(gun.Dead);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void Each_kind_of_target_is_worth_its_own_points()
    {
        foreach (var (kind, points) in new[] { (TargetKind.Turret, 200), (TargetKind.Radar, 1000), (TargetKind.Plane, 50), (TargetKind.Silo, 150) })
        {
            var g = Game();
            g.Put(0, Field.MinZ);
            g.Place(kind, 120, 0);
            g.Run(10, Stick(fire: true));
            g.Idle(40);
            Assert.Equal(points, g.Score);
        }
    }

    [Fact]
    public void Flying_into_something_on_the_deck_wrecks_the_ship()
    {
        var g = Game();
        g.Put(0, 6);
        g.Place(TargetKind.Radar, 60, 0);
        g.Idle(90);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("CRASHED", g.Cause);
    }

    [Fact]
    public void A_wall_lets_the_ship_through_its_hole_and_nowhere_else()
    {
        var g = Game();
        g.Put(10, 10);
        g.Wall(100, 0, 24, 20);
        g.Idle(150);
        Assert.Equal(Phase.Flying, g.Phase);

        // Beside the hole.
        g = Game();
        g.Put(-20, 10);
        g.Wall(100, 0, 24, 20);
        g.Idle(150);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("HIT THE WALL", g.Cause);

        // In line with it but too high.
        g = Game();
        g.Put(10, 30);
        g.Wall(100, 0, 24, 20);
        g.Idle(150);
        Assert.Equal(Phase.Dying, g.Phase);
    }

    [Fact]
    public void A_wall_stops_shots_except_through_its_hole()
    {
        var g = Game();
        g.Put(-30, 6);
        g.Wall(60, 0, 24, 20);
        var gun = g.Place(TargetKind.Turret, 150, -30);
        g.Run(20, Stick(fire: true));
        Assert.Contains(g.Effects, e => e.Kind == EffectKind.Spark);
        g.Idle(20);
        Assert.False(gun.Dead);
    }

    [Fact]
    public void A_force_field_must_be_flown_under()
    {
        var g = Game();
        g.Put(0, 10);
        g.ForceField(100, 18);
        g.Idle(150);
        Assert.Equal(Phase.Flying, g.Phase);

        g = Game();
        g.Put(0, 30);
        g.ForceField(100, 18);
        g.Idle(150);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("FORCE FIELD", g.Cause);
    }

    [Fact]
    public void Fuel_burns_over_the_fortress_and_running_dry_brings_the_ship_down()
    {
        var g = Game();
        g.Idle(60);
        Assert.Equal(GameEngine.FuelMax - GameEngine.FuelDrain, g.Fuel, 1);
        g.SetFuel(1);
        g.Idle(60);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("OUT OF FUEL", g.Cause);
    }

    [Fact]
    public void Guns_shoot_at_the_ship_and_a_shell_brings_it_down()
    {
        var g = Game();
        g.Calm = false;
        g.Put(0, 10);
        g.Place(TargetKind.Turret, 200, 20);
        g.Run(10, Stick());
        Assert.NotEmpty(g.Shells);
        g.Idle(180);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("SHOT DOWN", g.Cause);
    }

    [Fact]
    public void A_silo_sends_a_rocket_up_as_the_ship_comes_and_the_rocket_can_be_shot()
    {
        var g = Game();
        g.Calm = false;
        g.Put(20, 20);
        g.Place(TargetKind.Silo, 200, 0);
        g.Idle(70);
        var rocket = Assert.Single(g.Rockets);
        double z = rocket.Z;
        g.Idle(10);
        Assert.True(rocket.Z > z);
        // Line up with it and shoot, aiming above it: it climbs while the shot is on its way.
        g.Put(0, rocket.Z + 12);
        g.Run(3, Stick(fire: true));
        g.Idle(30);
        Assert.Empty(g.Rockets);
        Assert.Equal(200, g.Score);
    }

    // ───────────────────────────── the sections ─────────────────────────────

    [Fact]
    public void Every_level_runs_fortress_space_inner_fortress_then_the_robot()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.StartGame();
        Assert.Equal(Section.Fortress, g.Section);
        Assert.Contains(g.Targets, t => t.Kind == TargetKind.FuelTank);
        Assert.Contains(g.Barriers, b => !b.IsField);
        var seen = new List<Section> { g.Section };
        for (int t = 0; t < 60 * 300 && g.Level == 1; t++)
        {
            g.SetFuel(GameEngine.FuelMax);
            // Clear the way so the ship flies straight through.
            g.Barriers.Clear();
            g.Targets.Clear();
            g.Fighters.ForEach(f => f.Z = Field.MaxZ + 20);
            g.Tick(new Engine.Controls(0, 0, true));
            if (g.Section != seen[^1]) seen.Add(g.Section);
            if (g.Section == Section.Robot && g.Robot is { } r)
            {
                // Put the ship in line with the launcher.
                var (_, ly, lz) = r.Launcher;
                g.Put(ly, lz);
            }
        }
        Assert.Equal([Section.Fortress, Section.Space, Section.Inner, Section.Robot, Section.Fortress], seen);
        Assert.Equal(2, g.Level);
    }

    [Fact]
    public void Each_fighter_in_space_is_counted_down_shot_or_passed()
    {
        var g = Game(deck: false);
        g.StartAt(1, Section.Space);
        g.Clear(deck: false);
        g.EndSpaceSpawning();
        int left = g.PlanesLeft;
        Assert.True(left >= 12);
        g.Put(0, 30);
        g.AddFighter(150, 0, 30);
        g.Run(6, Stick(fire: true));
        g.Idle(30);
        Assert.Equal(left - 1, g.PlanesLeft);
        Assert.Equal(150, g.Score);
        // One that flies past is gone too.
        g.AddFighter(150, 40, 50);
        g.Idle(400);
        Assert.Equal(left - 2, g.PlanesLeft);
        Assert.Equal(Phase.Flying, g.Phase);
    }

    [Fact]
    public void Flying_into_a_fighter_destroys_both()
    {
        var g = Game(deck: false);
        g.Put(0, 30);
        g.AddFighter(120, 0, 30);
        g.Idle(200);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("COLLISION", g.Cause);
    }

    // ───────────────────────────── the robot ─────────────────────────────

    [Fact]
    public void Six_hits_on_the_launcher_bring_the_robot_down_and_clear_the_level()
    {
        var g = Game();
        var r = g.MeetRobot();
        int hits = 0;
        for (int t = 0; t < 60 * 8 && !r.Dead; t++)
        {
            var (_, ly, lz) = r.Launcher;
            g.Put(ly, lz);
            r.Charge = 0;
            g.Tick(Stick(fire: true));
            hits = r.Hits;
        }
        Assert.True(r.Dead);
        Assert.Equal(Robot.HitsToKill, hits);
        Assert.Equal(Phase.LevelClear, g.Phase);
        Assert.True(g.Score >= 3000 + 6 * 200);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Level);
        Assert.Equal(Section.Fortress, g.Section);
    }

    [Fact]
    public void Shots_that_miss_the_launcher_clang_off_the_robot()
    {
        var g = Game();
        var r = g.MeetRobot();
        g.Put(r.Y + 5, 40);
        g.Run(10, Stick(fire: true));
        g.Idle(30);
        Assert.Equal(0, r.Hits);
        Assert.Contains(g.Effects, e => e.Kind == EffectKind.Spark);
    }

    [Fact]
    public void Left_alone_the_robot_fires_a_homing_missile_that_can_be_shot_down()
    {
        var g = Game();
        var r = g.MeetRobot();
        g.Put(-45, 50);
        g.Idle((int)(60 * g.ChargeTime) + 5);
        var m = Assert.IsType<Bolt>(g.Missile);
        // It turns towards the ship.
        double y = m.Y;
        g.Idle(20);
        Assert.True(m.Y < y);
        g.Put(m.Y, m.Z);
        g.Run(3, Stick(fire: true));
        g.Idle(40);
        Assert.Null(g.Missile);
        Assert.Equal(500, g.Score);
        _ = r;
    }

    [Fact]
    public void The_homing_missile_hunts_down_a_ship_that_sits_still()
    {
        var g = Game();
        g.MeetRobot();
        g.Put(-10, 30);
        for (int t = 0; t < 60 * (g.ChargeTime + 6) && g.Phase == Phase.Flying; t++) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("HOMING MISSILE", g.Cause);
    }

    // ───────────────────────────── ships and scores ─────────────────────────────

    [Fact]
    public void A_lost_ship_comes_back_at_the_start_of_the_section_and_the_last_ends_the_game()
    {
        var g = Game();
        Assert.Equal(3, g.Lives);
        g.SetFuel(0.01);
        g.Idle(2);
        Assert.Equal(2, g.Lives);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(Phase.Flying, g.Phase);
        Assert.Equal(GameEngine.FuelMax, g.Fuel, 0);
        Assert.Equal(Section.Fortress, g.Section);
        Assert.True(g.DeckStart > g.Ship.X);
        g.SetLives(1);
        g.SetFuel(0.01);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 10);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Ten_thousand_points_earn_an_extra_ship()
    {
        var g = Game();
        g.SetScore(9900);
        g.Put(0, Field.MinZ);
        g.Place(TargetKind.Turret, 120, 0);
        g.Run(10, Stick(fire: true));
        g.Idle(30);
        Assert.Equal(4, g.Lives);
    }

    [Fact]
    public void The_fortress_is_laid_out_the_same_every_time_with_fuel_to_be_had()
    {
        string Layout(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            return string.Join(";", g.Targets.Select(t => $"{t.Kind}{t.X - g.DeckStart:0},{t.Y:0}")) + "|" + string.Join(";", g.Barriers.Select(b => $"{b.X - g.DeckStart:0}{b.IsField}"));
        }
        Assert.Equal(Layout(1), Layout(99));
        var game = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        foreach (int level in new[] { 1, 2, 3, 5 })
        {
            game.StartGame();
            game.StartAt(level);
            Assert.True(game.Targets.Count(t => t.Kind == TargetKind.FuelTank) >= 4);
            Assert.True(game.Barriers.Count >= 3);
            // Every wall's hole is big enough to fly through.
            Assert.All(game.Barriers.Where(b => !b.IsField), b => Assert.True(b.GapHi - b.GapLo >= 24 && b.GapTop >= 16));
        }
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_computer_pilot_clears_the_first_level_without_losing_a_ship()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        for (int t = 0; t < 60 * 200 && g.Level == 1 && g.State == GameState.Playing; t++)
            g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.Level);
        // Three ships still, or more with the extra ship at ten thousand.
        Assert.True(g.Lives >= 3);
        Assert.True(g.Score > 8000);
    }

    [Fact]
    public void Left_alone_the_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 20);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 20)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 60; t++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Ship.Y:0.000} {g.Ship.Z:0.000} {g.Lives} {g.Section} {g.Shells.Count}";
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true };
        g.StartGame();
        g.SetScore(99999);
        g.SetLives(1);
        g.SetFuel(0.01);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 4)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "ZAX") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("ZAX", 99999, 1), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}
