# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Minesweeper 2.0: a C# (.NET 8) game for Windows desktop (WinForms) and the web (Blazor WebAssembly), with four modes chosen on a start screen: classic square grid, hex sandbox (same Beginner/Intermediate/Expert/Custom options), Hex Challenge (20 levels with mystery "upside-down ?" tiles, clustered mines and a shrinking flag budget), and Endless Mode (rows slide down; unlocked by clearing all 20 challenge levels without placing a flag). Players have profiles with their own scores. The rules live in a UI-free library shared by both front ends (a mobile version is planned too). The owner plans to make the repo and a hosted web version public once everything is verified.

## Commands

Run from the repo root (`Minesweeper.sln`).

```bash
dotnet build Minesweeper.sln
dotnet test tests/Minesweeper.Core.Tests
dotnet test tests/Minesweeper.Core.Tests --filter "FullyQualifiedName~RevealingAllSafeCellsWins"   # single test
dotnet run --project src/Minesweeper.Desktop
# self-contained single-file exe (about 71 MB) into dist/
dotnet publish src/Minesweeper.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
dotnet run --project src/Minesweeper.Web --urls http://localhost:5210   # web dev server (restart it after changes)
dotnet publish src/Minesweeper.Web -c Release -o <folder>               # static site in <folder>/wwwroot
```

`.claude/launch.json` has a `web` entry for the browser preview on port 5211, because the owner often runs their own copy on 5210. Test a web release build too: it is what a host serves. Serve `<folder>/wwwroot` with any plain static file server (`.wasm` as `application/wasm`, `.dat`/`.blat` as octet-stream). To host below a domain root (GitHub Pages serves `/Minesweeper-2.0/`), change `<base href="/">` in `wwwroot/index.html`.

No linter is configured. The owner plays the published `dist/Minesweeper.Desktop.exe` (git-ignored), not `dotnet run`, so republish after any change they should see; it fails if an instance is running. The git remote `origin` is the private GitHub repo `jmorrison-51/Minesweeper-2.0` (branch `main`); the separate `jmorrison-51/Minesweeper` repo is the unrelated original game.

The shell is Git Bash on Windows (PowerShell for Windows-specific commands). Its working directory persists between calls, so use absolute paths or a subshell for `cd`.

To eyeball UI without driving the app, make a scratch WinExe project outside the repo that compiles `src/Minesweeper.Desktop/*.cs` (with its own `Program` class exposing `SavePath`/`Profiles` if you exclude the real one), build a form or `BoardControl`, call `DrawToBitmap` and read the PNG. `StartForm` accepts a sample player list for this.

## Architecture

### `src/Minesweeper.Core` (UI-free; must not reference WinForms or rendering)

- **`Board`** owns classic/hex/challenge rules: lazy mine placement on the first reveal, iterative flood-fill, flagging, chording, win/loss. Front ends read `board[x, y]` (a `Cell` struct) and `Status`, and mutate only through `Reveal`, `ToggleFlag` and `Chord`.
- **Topology:** `Difficulty.Shape` (`BoardShape.Square`/`Hex`) is honored only by `Board.Neighbors` (8-way square; 6-way "odd-r" hex, odd rows shifted right, pointy-top). Placement, counting, flood-fill and chording all go through it. `Difficulty.Key` ("Beginner" vs "Hex Beginner") keys best times.
- **Twists:** `BoardRules` (optional 3rd `Board` ctor arg) carries `FlagLimit`, `SafeStart`, `Clustering`, `MysteryFraction`/`MysteryThreshold`. `ChallengeLevel.Get(1..20)` is the level table. Mystery cells are numbered safe cells whose number `Board.IsNumberHidden` withholds until `MysteryNeeded` neighbors are revealed (capped by their safe-neighbor count so it can always be earned); chording a hidden number is refused; all numbers show once the game ends.
- **Guaranteed opener:** levels 9-20 have no safe start. `Board.OpeningGuessFailed` marks a game whose first click showed a lone number and whose next reveal hit a mine; `SaveData.ChallengeBadStarts` counts these in a row (any other decided start resets it), and at 2 `SaveData.ChallengeRules(level)` adds `SafeStart` for the next game. Wins record at least `SaveData.MinSolveMs` (1 s).
- **Flags:** capped at `FlagLimit` (the mine count unless a rule lowers it), so the counter never goes negative; the header shows `FlagsRemaining`. `FlagsPlaced` counts every flag the player places (even if removed) and deliberately excludes the flags `Win()` adds to all mines. A challenge win with `FlagsPlaced == 0` marks that level flagless (`SaveData.MarkFlaglessClear`); all 20 marked sets `SaveData.EndlessUnlocked`.
- **`EndlessBoard`** is a separate class, not a `Board`. Rows are keyed by serial number and the serial's parity gives the hex offset, so removing a cleared row leaves a gap instead of shifting the lattice. The next row's mines are generated one row ahead (hidden buffer) so numbers never change when rows arrive; removing a row recounts its neighbors so shown numbers always match visible mines, and `Settle` cascades zero cells and removes finished rows until stable. `Advance(seconds)` drives it; pace is `RowInterval(elapsed)`. Reaching the bottom or revealing a mine ends the run.
- **Persistence:** `SaveData` is per-player JSON, encrypted by `SaveCrypto` (AES-CBC + HMAC, keys derived from a phrase in the program). An edited, corrupt or plain-text file fails the HMAC, is copied to `<name>.invalid`, and the player starts fresh; only `LoadAny` (one-time upgrade from the old plain `save.json`) accepts plain JSON. Saves go through a temp file then a swap. Core takes file paths, so it stays platform-neutral. Browsers have no AES (WebAssembly throws `PlatformNotSupportedException`; SHA-256 and HMAC work), so `SaveData.ToSignedText`/`TryFromSignedText` store readable JSON signed with the same HMAC key: edits are still rejected.
- **`KeyValueProfileStore`** gives the same profile rules over any `IKeyValueStore` (browser localStorage, or `MemoryKeyValueStore` in tests and when storage is blocked): key `ms2.player.<lowercase name>` holds the typed name, a newline, then the signed save; `ms2.last` is the last player; an altered save is copied to `ms2.invalid.<name>` and the player starts fresh.
- **Profiles:** `ProfileStore` keeps `%AppData%\Minesweeper2\profiles\p_<name>.dat` per player (the `p_` prefix avoids reserved Windows names; names are 1-20 letters/digits/space/-/_, unique ignoring case) plus `last.txt`. `MigrateLegacy` moved the old `save.json` into a player called "Player".
- **Admin login:** the name `admin` in the New Player dialog asks for a password (`AdminAccess`, stored only as a SHA-256 hash; the file has the command to change it) and opens a separate, unlisted `admin` profile. `SaveData.AdminUnlock` (runtime only, never saved) unlocks Endless Mode for testing. Neither profile store's `List()` returns admin, so `Leaderboard` never ranks it. The same login works on the web.
- **`Leaderboard`:** pure ranking functions over either store's `LoadAll()` for each mode (fastest time per difficulty key, furthest challenge level, longest endless run).

### `src/Minesweeper.Desktop` (WinForms; no designer files, all UI built in code)

- **Flow:** `Program.Main` loops `ProfileForm` ("Who's playing?", also reached via Switch Player) then `StartForm` (mode picker plus scoreboards, tile-size choice), then the chosen form. `Program` holds the signed-in player (`CurrentProfile`, `IsAdmin`, `SavePath`, `LoadSave()`); every form loads and saves through those. `ReturnToMenu` (Game > Main Menu) returns to `StartForm`.
- **`MainForm`** serves `GameMode.Classic`, `Hex` and `HexChallenge` (challenge menu: Retry F2, Next F3, Choose Level F4 via `LevelSelectDialog`). It owns the `Board`, stopwatch and save, and lays out header and board by hand (DPI-scaled with `LogicalToDeviceUnits`, tiles shrink only if the window would not fit the screen). **`EndlessForm`** does the same for `GameMode.Endless`.
- **`BoardControl`** (square and hex geometry; hex hit-testing by nearest cell center) draws with GDI+ and turns mouse input into `Board` calls. **`EndlessControl`** draws rows at fractional positions for smooth scrolling and reveals on mouse *down* because rows keep moving; a 30 ms timer feeds `Advance` real elapsed time. It shares tile drawing with `BoardControl` through `internal static` members (`DrawHexBackground`, `DrawMine`, `HexCorners`, colors).
- **Saving and errors:** forms save through `Program.Save`, which warns once if `SaveData.TrySave` fails. Forms holding a save implement `ISavesProgress` so the `Application.ThreadException` handler can save them, append to `%AppData%\Minesweeper2\crash.log` and offer to carry on. A named mutex allows one instance. Form timers are not owned by the form, so each form disposes its timer in `FormClosed` (a live timer keeps ticking in the next screen).
- **Keyboard:** the forms' `ProcessCmdKey` hands keys to `BoardControl.HandleKey` / `EndlessControl.HandleKey` (arrows/WASD move a cursor, Space/Enter reveal or chord, F flags). Endless pauses on P/Esc and on deactivate, hiding the field; a run abandoned by F2 or closing still counts. Help > Controls (F1) is `ControlsHelp`.
- `ModeButton` (in `StartForm.cs`) is the retro raised button used on the start, profile and tile-size UI; `LedDisplay` and `FaceButton` are the header controls.

### `src/Minesweeper.Web` (Blazor WebAssembly, standalone static site)

- **One page, no routing** (so it works from any folder on a static host): `App.razor` switches between `Screens/` (`ProfilesScreen`, `StartScreen`, `BoardScreen` for Classic/Hex/HexChallenge, `EndlessScreen`) on `GameSession.Screen`, inside an `ErrorBoundary` whose error panel offers "Back to the main menu". `GameSession` (singleton) holds the player, one shared `SaveData`, and `Persist()`, which sets `SaveError` for the save-failed banner.
- **Drawing:** boards are SVG with one `<g>` per tile, so the browser does hit-testing. `Components/TileArt.razor` has the tile drawing as static `RenderFragment`s (same look as the desktop). Razor reserves `<text>` in code blocks, so SVG text is built with the builder API. `InvariantGlobalization` keeps SVG numbers printed with dots. `BoardView.SizeStyle` shrinks a board to fit the window both ways (CSS `min()` over viewport units, since percentages don't work inside the shrink-to-fit game window), using `--board-side`/`--board-chrome` in app.css for the space around it; it stops at `BoardView.MinTile` (28 px), and a smaller window scrolls the whole page (the board is never clipped into a scrolling frame; `.game-column` uses `margin: 0 auto` so an over-wide board starts at the left edge instead of being cut off by centering). Endless passes 14 px instead, so its whole field (and the danger line) stays visible in more window sizes.
- **Input:** `BoardView` does left/right/middle/both-button like the desktop, plus click-a-number to chord, long press (context menu event) to flag, and a Flag mode toggle for touch. `wwwroot/js/ms2.js` sends game keys to the current screen's `[JSInvokable] OnKey` (ignored while typing or when a `.dialog-backdrop` is open), reports focus loss and page hide for Endless, and wraps localStorage. Listeners return an id and `stop(id)` ignores stale ids, because the next screen can start listening before the old one is disposed.
- **Endless:** a `Task.Delay(30)` loop calls `Advance`. Each row is an `EndlessRow` component moved by a `transform` on its wrapper and redrawn only when its tiles change (`ShouldRender` with a hash). Runs are recorded before leaving (`ToMenu`, F2, `OnPageHide`), because the start screen reads scores before the old screen is disposed and closing a tab disposes nothing.
- **Gotchas:** a menu or dialog handler runs in that child component, so `MenuBar` raises `OnChosen` to make the screen redraw, and it tracks the open menu by title because screens rebuild menus on every redraw. Forms use `novalidate` (the game clamps values itself; a failed browser check silently blocks submit) and hold an off-screen submit button so Enter works with several fields. The preview pane throttles `requestAnimationFrame`, so measure animation with timers.

### Tests

`tests/Minesweeper.Core.Tests` is xUnit and covers all of Core (`Board`, `EndlessBoard`, `ChallengeLevel`, encryption, signed text, both profile stores, admin, `Leaderboard`) using seeded `Random` for determinism; classes that take a `Random` do so for this reason. It has no UI tests; check the web UI in the browser preview.

## Gotchas

- The `Face` enum (`FaceButton.cs`) shares its name with the `Face` color field in `BoardControl`; `MainForm` names its color `Gray` for this reason.
- On loss, `Board` reveals all unflagged mines, but wrongly flagged cells stay `Flagged`; the UI detects them by `State == Flagged && !IsMine`.
- Best times are recorded for Beginner, Intermediate and Expert (square and hex), not Custom.
- Known gap: the web version has no guard against two tabs of the same site; each keeps its own `SaveData` and the last save wins (the desktop's single-instance mutex has no web equivalent yet).
