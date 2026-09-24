# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Minesweeper 2.0: a C# (.NET 8) Windows desktop Minesweeper with four modes picked on a start screen (the fourth, Endless Mode, unlocks once every Hex Challenge level is cleared without placing a flag): classic square grid, hex sandbox (same Beginner/Intermediate/Expert/Custom options), and Hex Challenge (20 levels with rising difficulty, mystery "upside-down ?" tiles, clustered mines and a shrinking flag budget, with saved progress). Mobile and web versions are planned later.

## Commands

Run from the repo root (`Minesweeper.sln`).

```bash
dotnet build Minesweeper.sln
dotnet test tests/Minesweeper.Core.Tests
dotnet test tests/Minesweeper.Core.Tests --filter "FullyQualifiedName~RevealingAllSafeCellsWins"   # single test
dotnet run --project src/Minesweeper.Desktop
# self-contained single-file exe (about 71 MB) into dist/
dotnet publish src/Minesweeper.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

There is no linter configured. Publishing overwrites `dist/Minesweeper.Desktop.exe`, so close any running instance first. The Bash tool here is Git Bash on Windows; use PowerShell for Windows-specific commands.

## Architecture

- `src/Minesweeper.Core` is a UI-free class library (rules, state, persistence). It must not reference WinForms or any rendering code, because future hex, mobile and web front ends are meant to reuse it.
  - `Board` owns all game state and rules: lazy mine placement on the first reveal (the clicked cell is always safe), iterative flood-fill reveal, flagging, chording, and win/loss detection. Front ends read state through `board[x, y]` (a `Cell` struct) and `Board.Status`, and mutate it only through `Reveal`, `ToggleFlag` and `Chord`.
  - `Difficulty.Shape` (`BoardShape.Square` or `Hex`) selects the topology. `Board.Neighbors` is the only shape-aware code: 8-way for square, 6-way "odd-r" offset (odd rows shifted right, pointy-top) for hex. Mine placement, counting, flood-fill and chording all go through `Neighbors`. `Difficulty.Key` ("Beginner" / "Hex Beginner") is what best times are keyed by. Hex reuses the classic sizes and mine counts (Custom included).
  - `BoardRules` (optional 3rd `Board` ctor arg) carries the twists: `FlagLimit`, `SafeStart` (first click protects its neighbors), `Clustering` (chance a mine is placed beside an existing one), `MysteryFraction`/`MysteryThreshold`. `ChallengeLevel.Get(1..20)` is the level table (size 9x9 to 24x16, mines 10 to 88; twists phase in: mystery tiles and clustering from level 6, tight first click from 9, flag budget shrinking from 11). Mystery cells are numbered safe cells whose number `Board.IsNumberHidden` withholds until `MysteryNeeded` neighbors are revealed (capped by their safe-neighbor count so it can always be earned); chording a hidden number is refused, and all numbers show once the game ends. Cleared levels unlock the next via `SaveData.CompleteChallengeLevel`.
  - "Flagless" tracking: `Board.FlagsPlaced` counts every flag the player places (even ones later removed) and deliberately excludes the flags `Board.Win()` adds to every mine. `MainForm` marks a challenge level flagless (`SaveData.MarkFlaglessClear`) when a win has `FlagsPlaced == 0`. `SaveData.EndlessUnlocked` is true once all 20 levels are marked; the start screen shows the Endless button locked (darker, titled "Beat Hex Challenge without using any flags") until then.
  - `EndlessBoard` is a separate UI-free class (not a `Board`): 16-wide hex rows slide down, a new row is added at the top each time a row's worth of space opens, and clearing every safe cell in a row removes it. Rows are keyed by serial number, and the serial's parity gives its hex offset, so clearing a row leaves a gap instead of shifting the lattice. The next row's mines are generated one row ahead (hidden buffer) so numbers never change on arrival; removing a row recounts its neighbors so shown numbers always match visible mines, and zero cells cascade after recounts and arrivals (`Settle`). No flags. Pace is `RowInterval(elapsed)` (10 s per row at the start, speeding up to a 1.8 s floor). Reaching the bottom or revealing a mine ends the run; records are `SaveData.EndlessBestMs` / `EndlessBestRows`.
  - Flags are capped at `FlagLimit` (the mine count unless a rule lowers it): `ToggleFlag` ignores a new flag once `FlagCount == MineCount`, so the counter never goes negative. The header shows `FlagsRemaining`.
  - `SaveData` is JSON persistence (last difficulty per shape, custom-field settings, tile size 24/36/48 chosen on the start screen, best times in ms per difficulty key). It takes a file path, so Core stays platform-neutral.
- `src/Minesweeper.Desktop` is the WinForms front end. It has no designer files and all UI is built in code.
  - `Program` loops (`GameMode.Endless` opens `EndlessForm`, the others open `MainForm`): `StartForm` (mode picker, sets `Selected`) then the chosen game form. `MainForm.ReturnToMenu` (Game > Main Menu) sends the user back to the start screen. All three `GameMode`s open `MainForm`, which adapts: `Classic` uses square boards, `Hex` uses hex boards with the difficulty menu, and `HexChallenge` plays `ChallengeLevel`s (menu: Retry Level F2, Next Level F3, Choose Level F4 via `LevelSelectDialog`).
  - `MainForm` is the coordinator for classic and hex sandbox play. It builds the menu, owns the `Board`, the stopwatch and timer, and the save file (`%AppData%\Minesweeper2\save.json`), and lays out the header and board manually (DPI-scaled with `LogicalToDeviceUnits`). It draws the sunken bevels itself in `OnPaint`.
  - `BoardControl` (square and hex geometry, including hex hit-testing by nearest cell center) is a custom-painted control that draws cells with GDI+ and translates mouse input (including chord and pressed-cell visuals) into `Board` calls. It raises `Changed` after each input and `PressingChanged` for the face button.
  - `EndlessForm` / `EndlessControl` host `EndlessBoard`: a 30 ms timer feeds `Advance` real elapsed time (capped at 0.25 s per tick), the control draws rows at fractional positions for smooth scrolling and reveals on mouse *down* (rows keep moving). It shares tile drawing with `BoardControl` through its `internal static` `DrawHexBackground`, `DrawMine`, `HexCorners` and colors.
  - `LedDisplay` and `FaceButton` are small custom-painted header controls.
- `tests/Minesweeper.Core.Tests` is xUnit and covers `Board` and `SaveData` with seeded `Random` for determinism. `Board` accepts a `Random` in its constructor for this reason.

## Gotchas

- The `Face` enum (in `FaceButton.cs`) shares its name with the private `Face` color field in `BoardControl`. `MainForm` names its color `Gray` for this reason.
- On loss, `Board` reveals all unflagged mines, but wrongly flagged cells stay `Flagged`; the UI detects them by `State == Flagged && !IsMine`.
- Best times are only recorded for Beginner, Intermediate and Expert, not Custom.
