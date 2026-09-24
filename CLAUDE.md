# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Minesweeper 2.0: a C# (.NET 8) Windows desktop game with four modes chosen on a start screen: classic square grid, hex sandbox (same Beginner/Intermediate/Expert/Custom options), Hex Challenge (20 levels with mystery "upside-down ?" tiles, clustered mines and a shrinking flag budget), and Endless Mode (rows slide down; unlocked by clearing all 20 challenge levels without placing a flag). Players have profiles with their own scores. Mobile and web versions are planned, which is why the rules live in a UI-free library.

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

No linter is configured. The owner plays the published `dist/Minesweeper.Desktop.exe` (git-ignored), not `dotnet run`, so republish after any change they should see; it fails if an instance is running. There is a local git repo with no remote.

The shell is Git Bash on Windows (PowerShell for Windows-specific commands). Its working directory persists between calls, so use absolute paths or a subshell for `cd`.

To eyeball UI without driving the app, make a scratch WinExe project outside the repo that compiles `src/Minesweeper.Desktop/*.cs` (with its own `Program` class exposing `SavePath`/`Profiles` if you exclude the real one), build a form or `BoardControl`, call `DrawToBitmap` and read the PNG. `StartForm` accepts a sample player list for this.

## Architecture

### `src/Minesweeper.Core` (UI-free; must not reference WinForms or rendering)

- **`Board`** owns classic/hex/challenge rules: lazy mine placement on the first reveal, iterative flood-fill, flagging, chording, win/loss. Front ends read `board[x, y]` (a `Cell` struct) and `Status`, and mutate only through `Reveal`, `ToggleFlag` and `Chord`.
- **Topology:** `Difficulty.Shape` (`BoardShape.Square`/`Hex`) is honored only by `Board.Neighbors` (8-way square; 6-way "odd-r" hex, odd rows shifted right, pointy-top). Placement, counting, flood-fill and chording all go through it. `Difficulty.Key` ("Beginner" vs "Hex Beginner") keys best times.
- **Twists:** `BoardRules` (optional 3rd `Board` ctor arg) carries `FlagLimit`, `SafeStart`, `Clustering`, `MysteryFraction`/`MysteryThreshold`. `ChallengeLevel.Get(1..20)` is the level table. Mystery cells are numbered safe cells whose number `Board.IsNumberHidden` withholds until `MysteryNeeded` neighbors are revealed (capped by their safe-neighbor count so it can always be earned); chording a hidden number is refused; all numbers show once the game ends.
- **Flags:** capped at `FlagLimit` (the mine count unless a rule lowers it), so the counter never goes negative; the header shows `FlagsRemaining`. `FlagsPlaced` counts every flag the player places (even if removed) and deliberately excludes the flags `Win()` adds to all mines. A challenge win with `FlagsPlaced == 0` marks that level flagless (`SaveData.MarkFlaglessClear`); all 20 marked sets `SaveData.EndlessUnlocked`.
- **`EndlessBoard`** is a separate class, not a `Board`. Rows are keyed by serial number and the serial's parity gives the hex offset, so removing a cleared row leaves a gap instead of shifting the lattice. The next row's mines are generated one row ahead (hidden buffer) so numbers never change when rows arrive; removing a row recounts its neighbors so shown numbers always match visible mines, and `Settle` cascades zero cells and removes finished rows until stable. `Advance(seconds)` drives it; pace is `RowInterval(elapsed)`. Reaching the bottom or revealing a mine ends the run.
- **Persistence:** `SaveData` is per-player JSON, encrypted by `SaveCrypto` (AES-CBC + HMAC, keys derived from a phrase in the program). An edited, corrupt or plain-text file fails the HMAC, is copied to `<name>.invalid`, and the player starts fresh; only `LoadAny` (one-time upgrade from the old plain `save.json`) accepts plain JSON. Saves go through a temp file then a swap. Core takes file paths, so it stays platform-neutral.
- **Profiles:** `ProfileStore` keeps `%AppData%\Minesweeper2\profiles\p_<name>.dat` per player (the `p_` prefix avoids reserved Windows names; names are 1-20 letters/digits/space/-/_, unique ignoring case) plus `last.txt`. `MigrateLegacy` moved the old `save.json` into a player called "Player".
- **Admin login:** the name `admin` in the New Player dialog asks for a password (`AdminAccess`, stored only as a SHA-256 hash; the file has the command to change it) and opens a separate, unlisted `admin` profile. `SaveData.AdminUnlock` (runtime only, never saved) unlocks Endless Mode for testing. `ProfileStore.List()` never returns admin, so `Leaderboard` never ranks it.
- **`Leaderboard`:** pure ranking functions over `ProfileStore.LoadAll()` for each mode (fastest time per difficulty key, furthest challenge level, longest endless run).

### `src/Minesweeper.Desktop` (WinForms; no designer files, all UI built in code)

- **Flow:** `Program.Main` loops `ProfileForm` ("Who's playing?", also reached via Switch Player) then `StartForm` (mode picker plus scoreboards, tile-size choice), then the chosen form. `Program` holds the signed-in player (`CurrentProfile`, `IsAdmin`, `SavePath`, `LoadSave()`); every form loads and saves through those. `ReturnToMenu` (Game > Main Menu) returns to `StartForm`.
- **`MainForm`** serves `GameMode.Classic`, `Hex` and `HexChallenge` (challenge menu: Retry F2, Next F3, Choose Level F4 via `LevelSelectDialog`). It owns the `Board`, stopwatch and save, and lays out header and board by hand (DPI-scaled with `LogicalToDeviceUnits`, tiles shrink only if the window would not fit the screen). **`EndlessForm`** does the same for `GameMode.Endless`.
- **`BoardControl`** (square and hex geometry; hex hit-testing by nearest cell center) draws with GDI+ and turns mouse input into `Board` calls. **`EndlessControl`** draws rows at fractional positions for smooth scrolling and reveals on mouse *down* because rows keep moving; a 30 ms timer feeds `Advance` real elapsed time. It shares tile drawing with `BoardControl` through `internal static` members (`DrawHexBackground`, `DrawMine`, `HexCorners`, colors).
- `ModeButton` (in `StartForm.cs`) is the retro raised button used on the start, profile and tile-size UI; `LedDisplay` and `FaceButton` are the header controls.

### Tests

`tests/Minesweeper.Core.Tests` is xUnit and covers all of Core (`Board`, `EndlessBoard`, `ChallengeLevel`, encryption, profiles, admin, `Leaderboard`) using seeded `Random` for determinism; classes that take a `Random` do so for this reason. It has no UI tests.

## Gotchas

- The `Face` enum (`FaceButton.cs`) shares its name with the `Face` color field in `BoardControl`; `MainForm` names its color `Gray` for this reason.
- On loss, `Board` reveals all unflagged mines, but wrongly flagged cells stay `Flagged`; the UI detects them by `State == Flagged && !IsMine`.
- Best times are recorded for Beginner, Intermediate and Expert (square and hex), not Custom.
