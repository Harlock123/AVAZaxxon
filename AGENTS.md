# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

An isometric space shooter in the style of Sega's 1982 arcade game Zaxxon, in C# on .NET 10 and Avalonia 12.
One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in
code, and the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAZaxxon.slnx                       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAZaxxon.slnx                        # every test runs headless: no window opens
dotnet run --project src/AVAZaxxon                # play it; --no-audio for silence
ZAXXON_SHOTS=$PWD/Images dotnet test tests/AVAZaxxon.Tests --filter ScreenshotTests   # refresh the README's pictures
ZAXXON_PROBE=1 dotnet test tests/AVAZaxxon.Tests --filter PilotProbe   # report how the computer pilot does, level by level
python3 tools/make_sounds.py                      # rebuild src/AVAZaxxon/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAZaxxon/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores), lives and the fixed-step
    loop.
  - `GameEngine.Play.cs` builds each section of a level and steps it: the ship, fuel, walls and force fields,
    guns, fighters, the robot and its missile.
  - `Entities.cs` has `Controls`, `Field` (the world's axes and the step length), the sections and the objects.
  - `MachinePilot.cs` is the computer pilot, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAZaxxon/Views/`: `GameCanvas` draws the isometric view (`GameCanvas.Draw.cs`) and runs the timer.
- `src/AVAZaxxon/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAZaxxon/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVAZaxxon.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots and the `PilotProbe`
  tuning report.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`Field.Step`), so tests can fly whole levels deterministically from a seed. Draw random numbers from the
  engine's seeded `_rng`, never from `Random.Shared`. Each section's layout comes from its own
  `new Random(...)` seeded by the level and section, so it is the same every time; keep that.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls.Dy` is height, not screen position: +1 climbs and -1
  dives. Up on the keys, D-pad or stick climbs, so `InputHandler` negates its screen-down axis before building
  `Controls`. Keep every new control on that sign.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAZaxxon`); tests load
  the table from a fresh temp folder and set `Silent = true` on the engine. `Calm = true` keeps the guns from
  firing unless a test lets them.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
