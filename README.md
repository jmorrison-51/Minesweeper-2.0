# Minesweeper 2.0

Minesweeper with a hexagonal twist, for Windows and for the web. It is free to play and free to use under the
terms of the [AGPL-3.0 license](#license).

## Game modes

- **Minesweeper Original**: the classic square grid. Beginner, Intermediate, Expert or Custom.
- **Hex Minesweeper**: the same, on hexagonal tiles with six neighbors each.
- **Hex Challenge**: 20 levels of rising difficulty. Later levels add clustered mines, mystery tiles (an
  upside-down **?** hides a number until enough of its neighbors are open), a shrinking flag budget and no safe
  first click (a guaranteed opening is granted after repeated bad luck).
- **Endless Mode**: rows slide down toward you and new rows keep arriving. Clear them before they reach the
  bottom. It unlocks when you clear all 20 Hex Challenge levels without placing a single flag.

Every player has their own profile, best times and progress. Scores are kept on your own computer or browser;
there is no online leaderboard and no account.

## Controls

| | Mouse | Keyboard |
|---|---|---|
| Reveal | Left click | Space / Enter |
| Flag | Right click (web: long press, or the Flag mode button) | F |
| Reveal around a number | Click the number (web), middle click, or both buttons | Space / Enter on the number |
| Move | | Arrow keys or WASD |
| New game | Face button | F2 |
| Pause (Endless) | | P or Esc |

## Play

- **Web: [play it in your browser](https://jmorrison-51.github.io/Minesweeper-2.0/).** It works in any modern
  browser. Your saves live in that browser only, so use **Backup...** on the "Who's playing?" screen to keep a copy,
  or to move your players to another browser.
- **Windows:** download `Minesweeper.Desktop.exe` from the
  [latest release](https://github.com/jmorrison-51/Minesweeper-2.0/releases/latest) and run it (a single
  self-contained file of about 70 MB; nothing to install). Your saves are in `%AppData%\Minesweeper2`. The file is
  not code-signed, so Windows SmartScreen may say it "protected your PC": choose **More info**, then **Run anyway**.
  You can also build it yourself, below.

## Build it yourself

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build Minesweeper.sln
dotnet test tests/Minesweeper.Core.Tests

# Windows desktop version
dotnet run --project src/Minesweeper.Desktop

# Web version (dev server)
dotnet run --project src/Minesweeper.Web --urls http://localhost:5210
```

Single-file Windows executable:

```bash
dotnet publish src/Minesweeper.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

Static website (any plain static host works):

```bash
dotnet publish src/Minesweeper.Web -c Release -o site
# the site is in site/wwwroot
```

To host below a domain root (GitHub Pages serves `/Minesweeper-2.0/`), change `<base href="/">` in
`src/Minesweeper.Web/wwwroot/index.html` to match. Serve `.wasm` files as `application/wasm`.

## How it is put together

| Folder | What it is |
|---|---|
| `src/Minesweeper.Core` | The rules, saves and profiles. No user interface, shared by both versions. |
| `src/Minesweeper.Desktop` | The Windows version (WinForms). |
| `src/Minesweeper.Web` | The web version (Blazor WebAssembly). |
| `tests/Minesweeper.Core.Tests` | Automated tests for the rules and saves. |

## License

Copyright (C) 2026 jmorrison-51.

This program is free software: you can redistribute it and/or modify it under the terms of the
**GNU Affero General Public License, version 3** (see [LICENSE](LICENSE)).

In plain words: you may play, study, share and change it, but if you distribute this game or any part of its code,
or run a modified version for other people to use (including as a website), you must give everyone the complete
source code of your version under this same license. Nothing you build from it can be made closed or proprietary.
