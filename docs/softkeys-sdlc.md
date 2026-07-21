# softkeys — SDLC Project Document

**Project:** softkeys — Custom On-Screen Keyboard for Windows 11
**Stack:** C# / WPF / .NET 8 (self-contained single-file publish)
**Version:** Draft 1.0 — 2026-07-21
**Status:** M1 approved and tagged `m1-native-core` 2026-07-21 (see §9); M2 in progress

---

## 1. Governance Model

| Role | Entity | Responsibility |
|---|---|---|
| Architect / Red team | Claude.ai | Requirements, design, review of implementer output, risk analysis |
| Implementer (Champion) | Claude Code | All code, builds, tests, commits; works only from approved documents |
| Approver | Nasser | Gate sign-off at each milestone; final acceptance |

**Rules of engagement**
- Claude Code implements only what is specified in this document. Deviations require a change request logged in §9.
- Evidence before mutation: every milestone ends with demonstrable output (running exe, passing test, screenshot) before the next begins.
- Each gate produces a git tag on the project repo (self-hosted cgit).

---

## 2. Project Charter

### 2.1 Problem Statement
The built-in Windows OSK (`osk.exe`) freezes reliably (~90% reproduction) when a screenshot is taken while it is visible, and intermittently during normal sessions (~6 freezes/session, ~60 s recovery). Root cause analysis attributes this to OSK's architecture: it is a focus-aware UI Automation client that blocks on cross-process queries to the foreground application (capture overlays, Java accessibility bridges, TSF). The freeze is structural and cannot be fixed by the user.

### 2.2 Solution Principle
Build a **stateless input emitter**: a keyboard window that never queries other processes. Draw buttons; on press, inject events via `SendInput`; done. No UI Automation, no caret tracking, no TSF interaction, no cross-process calls in the hot path. The freeze category is exited by design, not patched.

### 2.3 Success Criteria
1. Zero freezes during a full RTH trading session with Bookmap, MotiveWave, and Snipping Tool in active use.
2. Screenshot capture with keyboard visible: no freeze, and keyboard absent from the captured image (F-01 enabled).
3. Typing works in all daily applications: browsers, editors, terminals, Office, Bookmap, MotiveWave.
4. English and Arabic input both function via Windows layout switching.

### 2.4 Out of Scope (v1)
- Text prediction, autocomplete, caret following
- Automatic layout detection / on-key Arabic labels (deferred to backlog E-01)
- Touch gestures, swipe typing
- Numpad block (backlog E-02)

---

## 3. Phase 1 — Requirements

### 3.1 Functional Requirements

| ID | Requirement | Acceptance test ref |
|---|---|---|
| F-01 | **Capture invisibility.** Window sets `WDA_EXCLUDEFROMCAPTURE` via `SetWindowDisplayAffinity`. Toggleable from header bar (default ON). When ON, keyboard is absent from screenshots and recordings. | T-01 |
| F-02 | **No focus steal.** Window carries `WS_EX_NOACTIVATE` (+ `WS_EX_TOPMOST`). Clicking any key never removes focus from the target application. | T-02 |
| F-03 | **Key injection.** Keys injected via `SendInput` using scan codes (`KEYEVENTF_SCANCODE`), so the active Windows keyboard layout (EN/AR) determines the character produced. | T-03 |
| F-04 | **Full layout.** Five rows matching the existing vboard.py layout: number row, QWERTY rows, modifier row, arrow keys. Proportional key widths (Space widest; Backspace, Enter, Shift wider). | T-04 |
| F-05 | **Sticky modifiers.** Shift, Ctrl, Alt, Win act as one-shot latches: press to arm (visual pressed state), released automatically after the next non-modifier key. Left+Right Shift mutually exclusive. | T-05 |
| F-06 | **Shift labels.** While Shift is armed, symbol keys display their shifted character (` → ~, 1 → !, etc.); labels revert after emission. | T-05 |
| F-07 | **Auto-repeat.** Non-modifier key held ≥ 400 ms repeats at 100 ms intervals; repeat stops on release or pointer leave. | T-06 |
| F-08 | **Topmost-but-passive.** `HWND_TOPMOST` asserted once at startup. No re-assertion timers or z-order polling. | T-07 |
| F-09 | **Appearance settings.** Background color (preset palette), opacity (0.00–1.00 in 0.01 steps), automatic text-color contrast switch for light backgrounds. Controls in header bar, collapsible behind a ☰ toggle. | T-08 |
| F-10 | **Settings persistence.** Color, opacity, window size, and F-01 toggle state persisted to `%APPDATA%\softkeys\settings.json`; restored on startup; corrupt/missing file falls back to defaults without error dialogs. | T-09 |
| F-11 | **Resizable window.** Keys scale proportionally with window size; size persisted per F-10. | T-04 |

### 3.2 Non-Functional Requirements

| ID | Requirement |
|---|---|
| NF-01 | **Zero cross-process calls** in the input path. Forbidden APIs: UI Automation, `GetGUIThreadInfo` on foreign threads, `AttachThreadInput`, window hooks, TSF interfaces. |
| NF-02 | **Zero network code.** No sockets, no HTTP, no telemetry, no update checks. Verifiable by source inspection. |
| NF-03 | **Single-file deployment.** `dotnet publish` self-contained single-file exe for `win-x64`. No installer, no runtime prerequisite. |
| NF-04 | **No elevation.** Runs as standard user. Documented limitation: cannot type into elevated (admin) windows due to UIPI — this is accepted, not worked around. |
| NF-05 | **Resource budget.** Idle CPU ≈ 0%, memory < 150 MB, startup < 2 s on target hardware. |
| NF-06 | **DPI awareness.** Per-monitor DPI aware; crisp rendering on the 4K/high-DPI displays of HOME-GAMING-PC. |
| NF-07 | **Source hosting.** Repo on self-hosted cgit (`git.nasserhub.net`), tagged releases, no external CI dependency. |

### 3.3 Known Limitations (accepted at Gate 0)
- L-01: `SendInput` may be ignored by anti-cheat-protected games and elevated windows (UIPI). Accepted.
- L-02: With F-01 ON, the keyboard is also invisible in screen sharing and recordings. Mitigated by the toggle.
- L-03: Key labels are English-only in v1; Arabic characters are produced correctly (F-03) but not displayed on keys. Backlog E-01.

**GATE 0 — Approver signs off on §3 before design begins.**

---

## 4. Phase 2 — Design

### 4.1 Architecture

```
softkeys/
├── src/
│   ├── Program.cs                    # entry point: --selftest / --harness console modes, WPF bootstrap (CR-04)
│   ├── Harness.cs                    # M1 gate harness: types "test" into Notepad (CR-04)
│   ├── App.xaml / App.xaml.cs        # WPF application, single-instance guard
│   ├── MainWindow.xaml               # key grid + header bar (XAML layout)
│   ├── MainWindow.xaml.cs            # UI logic: sticky modifiers, repeat timers, settings UI
│   ├── Native/
│   │   ├── InputInjector.cs          # SendInput P/Invoke, INPUT/KEYBDINPUT structs, scan-code table
│   │   └── WindowStyles.cs           # WS_EX_NOACTIVATE, HWND_TOPMOST, SetWindowDisplayAffinity
│   ├── KeyMap.cs                     # key definitions: label, shifted label, scan code, width, is-modifier
│   └── Settings.cs                   # JSON load/save, defaults, %APPDATA% path
├── tests/
│   └── InputInjectorTests.cs         # struct-size assertions, scan-code table integrity
├── softkeys.csproj
└── README.md
```

### 4.2 Key Design Decisions

| ID | Decision | Rationale |
|---|---|---|
| D-01 | WPF `Grid` with proportional star-sized columns | Replaces manual width bookkeeping in vboard.py; Space=12*, standard key=2*, etc. |
| D-02 | Scan codes, not virtual-key codes, in `SendInput` | Respects active layout → Arabic works without app knowledge (F-03) |
| D-03 | Modifier injection order: press mods → press key → release key → release mods, in one `SendInput` array call | Atomic; no interleaving with physical keyboard input |
| D-04 | `WS_EX_NOACTIVATE` applied in `OnSourceInitialized` via `SetWindowLong` | Earliest point where HWND exists; WPF has no managed flag for it |
| D-05 | Arrow/nav keys marked `KEYEVENTF_EXTENDEDKEY` | Required for correct scan-code injection of extended keys |
| D-06 | `DispatcherTimer` for repeat delay (400 ms) and interval (100 ms) | Direct port of the GLib.timeout_add pattern |
| D-07 | Settings as JSON (`System.Text.Json`), not INI | Native to .NET, no dependency |
| D-08 | Struct layouts verified by `Marshal.SizeOf` assertions in tests | The classic P/Invoke failure mode (wrong INPUT size on x64) caught at test time, not silently at runtime |

### 4.3 Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| R-01: INPUT struct marshaling error on x64 | Keys silently not sent | D-08 test; known-good 40-byte layout |
| R-02: WPF click focus behavior overrides NOACTIVATE | Focus steal | `Focusable=False` on all buttons + `WindowStyle` handling; T-02 validates |
| R-03: `PreviewMouseDown` vs `Click` ordering breaks press/release repeat model | Repeat glitches | Use `PreviewMouseLeftButtonDown/Up` + `MouseLeave`, mirroring vboard.py's three handlers |
| R-04: Snipping Tool overlay interaction regression | The original bug reappears | T-01 executed as the flagship test; passive z-order per F-08 |

**GATE 1 — Approver signs off on §4 before implementation.**

---

## 5. Phase 3 — Implementation Plan (Claude Code)

| Milestone | Deliverable | Gate evidence |
|---|---|---|
| M1 — Native core | `InputInjector` + `WindowStyles` + console harness that types "test" into Notepad without stealing focus | Screen recording or witnessed run; struct tests green |
| M2 — Window shell | Borderless-capable WPF window: NOACTIVATE, topmost, opacity, capture-exclusion toggle working | T-01, T-02 pass |
| M3 — Full keyboard | Complete key grid, sticky modifiers, shift labels, auto-repeat | T-03 – T-06 pass |
| M4 — Settings & polish | Header bar controls, persistence, resize scaling, DPI check | T-07 – T-09 pass |
| M5 — Release | Single-file publish, README, repo pushed to cgit, tag `v1.0.0` | Exe hash recorded; acceptance sign-off |

Each milestone = one commit series + gate review by Approver before the next starts.

**GATE 2 — per-milestone approvals as above.**

---

## 6. Phase 4 — Test Plan (Acceptance)

| ID | Test | Pass criterion |
|---|---|---|
| T-01 | Open keyboard, F-01 ON, take screenshot with Snipping Tool; repeat 10× | Zero freezes of either program; keyboard absent from all 10 images |
| T-02 | Focus Notepad, click 20 keys | Notepad keeps focus the entire time; all characters land |
| T-03 | Switch Windows layout to Arabic, type letter row | Arabic characters produced; switch back to EN, Latin produced |
| T-04 | Resize window from minimum to full-screen | Keys scale proportionally; layout matches §3.1 F-04 |
| T-05 | Shift+a, Ctrl+c/Ctrl+v round trip, Win key opens Start | Correct results; modifier auto-releases; labels toggle |
| T-06 | Hold Backspace 3 s in a text field | Single delete, ~400 ms pause, then repeating deletes; stops on release |
| T-07 | Run 10 min with Bookmap + MotiveWave active; take 5 screenshots | Zero freezes; keyboard stays topmost without polling |
| T-08 | Change color to White, opacity to 0.50 | Text auto-switches to dark; opacity visibly applied |
| T-09 | Close and relaunch; then corrupt settings.json and relaunch | Settings restored; corrupt file → silent defaults |

---

## 7. Phase 5 — Deployment

1. `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
2. Record SHA-256 of the exe in README.
3. Push to `git.nasserhub.net` (new bare repo `softkeys`), tag `v1.0.0`, verify cgit agefile hook.
4. Place exe on HOME-GAMING-PC; optional Start Menu shortcut. No installer.

---

## 8. Phase 6 — Maintenance & Enhancement Backlog

| ID | Item | Notes |
|---|---|---|
| E-01 | Arabic key labels + layout-aware label switching | Display only; injection already works via D-02 |
| E-02 | Numpad block (toggleable) | Extends KeyMap |
| E-03 | Function-row (F1–F12) toggle | Present in vboard.py; deferred to keep v1 compact |
| E-04 | Themes beyond flat colors | Low priority |

---

## 9. Change Log / Change Requests

| Date | ID | Change | Approved |
|---|---|---|---|
| 2026-07-21 | — | Initial draft | Approved (superseded by CR-01/CR-02) |
| 2026-07-21 | CR-01 | **Gate 0 approved.** Requirements (§3) accepted as written, including §3.3 limitations. Open question resolved: F1–F12 row stays deferred to backlog E-03; not in v1. | Approved — Nasser |
| 2026-07-21 | CR-02 | **Gate 1 approved.** Design (§4) accepted as written. | Approved — Nasser |
| 2026-07-21 | CR-03 | **F-04 layout clarification.** "Matching the existing vboard.py layout" means vboard.py's exact row arrangement is authoritative: ↑ in the Shift row, ← → ↓ in the modifier row. Key widths per vboard.py `create_row()` values, mapped to star sizing per D-01. | Approved — Nasser |
| 2026-07-21 | CR-04 | **§4.1 tree amended.** `src/Program.cs` (console entry for M1; WPF bootstrap at M2) and `src/Harness.cs` (harness mandated by §5 M1) added to the architecture tree. | Approved — Nasser |
| 2026-07-21 | CR-05 | **Tooling note.** .NET SDK 8.0.423 installed user-local at `%LOCALAPPDATA%\Microsoft\dotnet` via the official install script; no elevation, profile-contained. Tooling only — not product scope. | Approved — Nasser |
| 2026-07-21 | CR-06 | **Status-line maintenance authorized as routine.** The document status line is kept current at each gate without further CRs. | Approved — Nasser |
| 2026-07-21 | — | **Gate 2 / M1 approved.** Witnessed run: selftest 111/111 green; harness typed "test" into Notepad under Approver focus. Tagged `m1-native-core`. | Approved — Nasser |
