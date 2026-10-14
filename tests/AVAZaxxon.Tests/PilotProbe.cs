using AVAZaxxon.Engine;

namespace AVAZaxxon.Tests;

/// <summary>How the computer pilot gets on, level by level (set ZAXXON_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_flies()
    {
        if (Environment.GetEnvironmentVariable("ZAXXON_PROBE") is null) return;
        for (int level = 1; level <= 3; level++)
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "zaxxon-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.StartAt(level);
            g.SetLives(50);
            var deaths = new List<string>();
            var last = g.Phase;
            double minFuel = 100;
            int t = 0;
            for (; t < 60 * 400 && g.Level == level && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths.Add($"{g.Cause}@{g.Section}");
                if (g.Section is Section.Fortress or Section.Inner) minFuel = Math.Min(minFuel, g.Fuel);
                last = g.Phase;
            }
            output.WriteLine($"level {level} seed {seed}: reached level {g.Level} {g.Section} after {t / 60.0:0}s score {g.Score} min fuel {minFuel:0} deaths {deaths.Count}: {string.Join(", ", deaths.GroupBy(d => d).Select(d => d.Key + " x" + d.Count()))}");
        }
    }
}
