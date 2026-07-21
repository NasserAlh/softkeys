# softkeys

**A freeze-proof on-screen keyboard for Windows 11 — invisible to screenshots, never steals focus, zero network code.**

![screenshot](docs/screenshot.png)

## Why this exists

The built-in Windows On-Screen Keyboard (`osk.exe`) froze on me about six times per session — and near-100% reproducibly the moment I took a screenshot with the Snipping Tool, locking up for around a minute each time. The cause is architectural: OSK is a UI Automation client that synchronously queries other processes (the focused app, capture overlays, the Text Services Framework), and when any of them doesn't answer, OSK's UI thread blocks until the timeout expires.

softkeys exits that design category entirely. It is a **stateless input emitter**: it never queries any other process. It draws keys; when you press one, it injects the event with `SendInput` and moves on. There is no cross-process call anywhere in the input path — so there is nothing to freeze on.

## Features

- **Invisible to screenshots and recordings** (toggleable) — uses `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, so the keyboard never appears in your captures. The built-in OSK can't do this.
- **Never steals focus** — `WS_EX_NOACTIVATE`: clicking keys types into your app; your app stays active the whole time.
- **Works with any keyboard layout, including RTL** — keys inject *scan codes*, so the active Windows layout decides the character. Switch to Arabic and the letter keys produce Arabic; no configuration.
- **Freeze-proof by design** — no UI Automation, no hooks into other processes, no caret tracking, no TSF.
- **Zero network code** — no telemetry, no update checks, no sockets. Auditable in minutes; the source is small.
- **Single portable exe** — self-contained .NET 8, no installer, no prerequisites, runs as standard user.
- Sticky modifiers (tap Shift/Ctrl/Alt/Win to arm, auto-release after the next key), shift-aware key labels, hold-to-repeat, adjustable color and opacity with automatic text contrast, resizable with proportional key scaling, settings persisted between sessions.

## Install

1. Download `softkeys.exe` from the [latest release](../../releases/latest).
2. Verify the hash (published in the release notes; for v1.0.0: `860fefe9149116a2d0b46dacc223f808d1e78f98db73de7aa87f9213c0bc91bd`):
   `certutil -hashfile softkeys.exe SHA256`
3. Run it. No installation, no admin rights.

**SmartScreen note:** the exe is unsigned (code-signing certificates cost real money; this is a free tool). Windows may warn on first run — "More info → Run anyway." If you'd rather not trust a downloaded binary, build it yourself in one command (below); the source is short enough to read in one sitting, and the zero-network-code claim is verifiable there.

## Build from source

Requires the .NET 8 SDK.

```
git clone https://github.com/NasserAlh/softkeys.git
cd softkeys
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The exe lands in `bin/Release/net8.0-windows/win-x64/publish/`.

Run the built-in self-test (structural checks on the injection layer, key map, and settings):

```
softkeys.exe --selftest
```

## Usage

- **☰** in the header bar shows/hides the settings (capture invisibility toggle, background color, opacity).
- **Drag the title strip** to move; **drag any edge or corner** to resize — keys scale proportionally.
- **Modifiers are sticky:** tap Shift, then a letter → one shifted character, Shift releases itself. Tap an armed modifier again to cancel it (nothing is typed). Win works as a chord: arm Win, tap E → Explorer.
- Settings are saved to `%APPDATA%\softkeys\settings.json`. Delete the file to reset; a corrupt file is silently replaced with defaults.

## Limitations (by design)

- Can't type into **elevated (admin) windows** — Windows blocks input from standard-user processes into elevated ones (UIPI). Run softkeys elevated yourself if you need this.
- Some games with anti-cheat ignore injected input.
- With capture invisibility ON, the keyboard is also invisible in **screen sharing** — toggle it off when presenting.
- A bare Win-key tap can't open the Start menu (the sticky-latch model only injects modifiers as part of a chord); the Start button is one click away anyway.
- Key labels are English-only for now; input follows the active layout regardless (Arabic tested end-to-end).

## Credits

softkeys began as a Windows reimplementation of **[vboard](https://github.com/mdev588/vboard)** by **mdev588** — a virtual keyboard for Linux (now archived). The layout, sticky-modifier behavior, color palette, and general spirit come from vboard; the original `vboard.py` is preserved in [`reference/`](reference/) under its LGPL-2.1 license. Thank you, mdev588.

The Windows implementation was developed with an AI-assisted, gated SDLC process — requirements, design decisions, risk register, and acceptance tests are all in [`docs/softkeys-sdlc.md`](docs/softkeys-sdlc.md), which some readers may find as interesting as the code.

## License

LGPL-2.1 — same as the original vboard. See [LICENSE](LICENSE).

## Contributing

Issues and PRs are welcome. This is a personal tool maintained as time allows — no promises on response time. Please run `--selftest` and test on a real Windows 11 machine before submitting.
