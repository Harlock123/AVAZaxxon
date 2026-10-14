using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAZaxxon.Engine;
using AVAZaxxon.Views;

namespace AVAZaxxon.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set ZAXXON_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avazaxxon-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("ZAXXON_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAZaxxon-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer fly until something worth a picture is on screen.</summary>
    private static bool FlyUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 240)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(200);
        c.Advance(0.6);
        Shot(w, c, "title");
        g.HighScores.Insert("RLR", 61250, 4);
        g.HighScores.Insert("AVA", 45800, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Flying_a_level()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(30);
        g.Idle(20);
        Shot(w, c, "fortress-ahead");
        // Lined up on the hole in a wall.
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Flying && g.Barriers.Any(b => !b.IsField && b.X - g.Ship.X is > 25 and < 45 && b.X - g.DeckStart > 200)));
        Shot(w, c, "wall");
        // A fuel tank going up.
        Assert.True(FlyUntil(g, c, g => g.Effects.Any(e => e.Kind == EffectKind.BigBoom && e.Age is > 0.15 and < 0.3) && g.Phase == Phase.Flying));
        Shot(w, c, "fuel");
        // Under a force field.
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Flying && g.Barriers.Any(b => b.IsField && b.X - g.Ship.X is > 15 and < 40)));
        Shot(w, c, "force-field");
        // Guns firing back.
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.Flying && g.Section == Section.Fortress && g.Shells.Count >= 2 && g.Shots.Count >= 2));
        Shot(w, c, "gunfire");
        // Open space.
        Assert.True(FlyUntil(g, c, g => g.Section == Section.Space && g.Phase == Phase.Flying && g.Fighters.Count(f => f.X - g.Ship.X < 260) >= 2 && g.Message == null));
        Shot(w, c, "space");
        Assert.True(FlyUntil(g, c, g => g.Section == Section.Inner && g.Phase == Phase.Flying && g.Targets.Count(t => !t.Dead && t.X - g.Ship.X is > 0 and < 250) >= 5 && g.Message == null));
        Shot(w, c, "inner-fortress");
        // The robot, charging.
        Assert.True(FlyUntil(g, c, g => g.Robot is { Dead: false, Hits: >= 1 } r && r.Charge > 0.5 && g.Shots.Count >= 2));
        // Let it charge up and fire, for the pictures.
        g.Robot!.Charge = g.ChargeTime * 0.7;
        c.Advance(0.016);
        Shot(w, c, "goliath");
        g.Robot!.Charge = g.ChargeTime - 0.02;
        g.Shots.Clear();
        Assert.True(FlyUntil(g, c, g => g.Missile is { } m && m.Age > 0.25));
        Shot(w, c, "homing-missile");
        Assert.True(FlyUntil(g, c, g => g.Phase == Phase.LevelClear && g.PhaseTime > 0.4));
        Shot(w, c, "level-clear");
    }

    [AvaloniaFact]
    public void Crashing()
    {
        var (w, c, g) = Open(4);
        g.StartGame();
        // Fly straight at the gate wall, too high to get through.
        for (int t = 0; t < 60 * 30 && g.Phase != Phase.Dying; t++)
        {
            g.Tick(new Engine.Controls(0, 1, false));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        g.Idle(14);
        c.Advance(0.2);
        Shot(w, c, "crash");
    }
}
