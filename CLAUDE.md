# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Minesweeper 2.0: a C# (.NET 8) Windows desktop Minesweeper with three modes picked on a start screen: classic square grid, hex sandbox (same Beginner/Intermediate/Expert/Custom options), and Hex Challenge (20 levels with rising difficulty, mystery "upside-down ?" tiles, clustered mines and a shrinking flag budget, with saved progress). Mobile and web versions are planned later.

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
  - Flags are capped at `FlagLimit` (the mine count unless a rule lowers it): `ToggleFlag` ignores a new flag once `FlagCount == MineCount`, so the counter never goes negative. The header shows `FlagsRemaining`.
  - `SaveData` is JSON persistence (last difficulty per shape, custom-field settings, tile size 24/36/48 chosen on the start screen, best times in ms per difficulty key). It takes a file path, so Core stays platform-neutral.
- `src/Minesweeper.Desktop` is the WinForms front end. It has no designer files and all UI is built in code.
  - `Program` loops: `StartForm` (mode picker, sets `Selected`) then the chosen game form. `MainForm.ReturnToMenu` (Game > Main Menu) sends the user back to the start screen. All three `GameMode`s open `MainForm`, which adapts: `Classic` uses square boards, `Hex` uses hex boards with the difficulty menu, and `HexChallenge` plays `ChallengeLevel`s (menu: Retry Level F2, Next Level F3, Choose Level F4 via `LevelSelectDialog`).
  - `MainForm` is the coordinator for classic and hex sandbox play. It builds the menu, owns the `Board`, the stopwatch and timer, and the save file (`%AppData%\Minesweeper2\save.json`), and lays out the header and board manually (DPI-scaled with `LogicalToDeviceUnits`). It draws the sunken bevels itself in `OnPaint`.
  - `BoardControl` (square and hex geometry, including hex hit-testing by nearest cell center) is a custom-painted control that draws cells with GDI+ and translates mouse input (including chord and pressed-cell visuals) into `Board` calls. It raises `Changed` after each input and `PressingChanged` for the face button.
  - `LedDisplay` and `FaceButton` are small custom-painted header controls.
- `tests/Minesweeper.Core.Tests` is xUnit and covers `Board` and `SaveData` with seeded `Random` for determinism. `Board` accepts a `Random` in its constructor for this reason.

## Gotchas

- The `Face` enum (in `FaceButton.cs`) shares its name with the private `Face` color field in `BoardControl`. `MainForm` names its color `Gray` for this reason.
- On loss, `Board` reveals all unflagged mines, but wrongly flagged cells stay `Flagged`; the UI detects them by `State == Flagged && !IsMine`.
- Best times are only recorded for Beginner, Intermediate and Expert, not Custom.
