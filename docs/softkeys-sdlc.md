# softkeys — SDLC Project Document

**Project:** softkeys — Custom On-Screen Keyboard for Windows 11
**Stack:** C# / WPF / .NET 8 (self-contained single-file publish)
**Version:** Draft 1.0 — 2026-07-21
**Status:** v1.2 (A2) — M9 in progress. Gate B0 approved 2026-07-21; Amendment A2 adopted as CR-19 (F-15 icon, F-16/F-17/F-18 installer/uninstall/upgrade, F-19 minimize, F-20 snap immunity). v1.1.0 released 2026-07-21 (Gate A2 + final acceptance). GitHub is the sole remote and canonical history per CR-17 + addendum (cgit archive deleted).

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
| F-11 | **Resizable window.** Keys scale proportionally with window size; size persisted per F-10. Maximize is blocked (CR-13); "full-screen" means edge-drag sizing, implemented via a WM_NCHITTEST edge band (CR-12). | T-04 |

### 3.2 Non-Functional Requirements

| ID | Requirement |
|---|---|
| NF-01 | **Zero cross-process calls** in the input path. Forbidden APIs: UI Automation, `GetGUIThreadInfo` on foreign threads, `AttachThreadInput`, window hooks, TSF interfaces. |
| NF-02 | **Zero network code.** No sockets, no HTTP, no telemetry, no update checks. Verifiable by source inspection. |
| NF-03 | **Single-file deployment.** `dotnet publish` self-contained single-file exe for `win-x64`. No installer, no runtime prerequisite. |
| NF-04 | **No elevation.** Runs as standard user. Documented limitation: cannot type into elevated (admin) windows due to UIPI — this is accepted, not worked around. |
| NF-05 | **Resource budget.** Idle CPU ≈ 0%, memory < 150 MB, startup < 2 s on target hardware. |
| NF-06 | **DPI awareness.** Per-monitor DPI aware; crisp rendering on the 4K/high-DPI displays of HOME-GAMING-PC. |
| NF-07 | **Source hosting.** Repo on self-hosted cgit (`git.nasserhub.net`), tagged releases, no external CI dependency. *Amended by CR-17 (2026-07-21): hosting is GitHub (`github.com/NasserAlh/softkeys`), the sole remote and system of record; the cgit repo is a frozen v1.0.0 archive.* |

### 3.3 Known Limitations (accepted at Gate 0)
- L-01: `SendInput` may be ignored by anti-cheat-protected games and elevated windows (UIPI). Accepted.
- L-02: With F-01 ON, the keyboard is also invisible in screen sharing and recordings. Mitigated by the toggle.
- L-03: Key labels are English-only in v1; Arabic characters are produced correctly (F-03) but not displayed on keys. Backlog E-01.
- L-04: Bare modifier taps are not injectable in the latch model — the second press of an armed modifier is a pure cancel, emitting nothing (CR-09). Start remains reachable via the taskbar.
- L-05: Arabic keycaps show the base (unshifted) Arabic glyph only; shifted Arabic characters (diacritics, tatweel, etc.) are produced correctly when typed but are not displayed on the caps (Amendment A1). Revisit only on user demand.
- L-06: The CapsLock indicator refreshes on softkeys' own events (after each emit, on pointer-enter) — never on a timer. Toggling CapsLock from the physical keyboard while softkeys is idle leaves the indicator stale until the next interaction. Deliberate consequence of the no-polling rule (D-10).
- L-07: `softkeys-setup.exe` is unsigned like the exe itself — SmartScreen will warn. Recorded in README and release notes with the same verify-hash-or-build-from-source guidance (Amendment A2).

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
│   ├── Settings.cs                   # JSON load/save, defaults, %APPDATA% path
│   └── app.manifest                  # per-monitor-v2 DPI awareness, NF-06 (CR-11)
├── tests/
│   ├── InputInjectorTests.cs         # struct-size assertions, scan-code table integrity
│   ├── KeyMapTests.cs                # layout/width/shift-pair integrity vs CR-03 (CR-10)
│   └── SettingsTests.cs              # F-10 silent-default fallback, roundtrip (CR-11)
├── softkeys.csproj
└── README.md
```

### 4.2 Key Design Decisions

| ID | Decision | Rationale |
|---|---|---|
| D-01 | WPF `Grid` with proportional star-sized columns | Replaces manual width bookkeeping in vboard.py; Space=12*, standard key=2*, etc. |
| D-02 | Scan codes, not virtual-key codes, in `SendInput` | Respects active layout → Arabic works without app knowledge (F-03) |
| D-03 | Modifier injection order: press mods → press key → release key → release mods. **As amended by CR-18:** a plain tap stays one `SendInput` call; a chord is three calls (modifier-downs / key tap / modifier-ups) with a 60 ms inter-call gap | Atomicity holds within each call; the gapped chord mimics physical key timing — the more faithful model, and required by targets that sample modifier state asynchronously at processing time (DF-01, Win11 Notepad) |
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
| M6 — Public mirror (CR-15) | Public GitHub repo `NasserAlh/softkeys` as second remote (`github`); `main` + all milestone tags + `v1.0.0` pushed after M5 acceptance | Repo public with `v1.0.0` visible; README hash matches released exe |

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
| T-05 | Shift+a, Ctrl+c/Ctrl+v round trip, Super+E chord opens Explorer (amended per CR-09) | Correct results; modifier auto-releases; labels toggle |
| T-06 | Hold Backspace 3 s in a text field | Single delete, ~400 ms pause, then repeating deletes; stops on release |
| T-07 | Run 10 min with Bookmap + MotiveWave active; take 5 screenshots | Zero freezes; keyboard stays topmost without polling |
| T-08 | Change color to White, opacity to 0.50 | Text auto-switches to dark; opacity visibly applied |
| T-09 | Close and relaunch; then corrupt settings.json and relaunch | Settings restored; corrupt file → silent defaults |

---

## 7. Phase 5 — Deployment

1. `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
2. Record SHA-256 of the exe in README.
3. Push to `git.nasserhub.net` (new bare repo `softkeys`), tag `v1.0.0`, verify cgit agefile hook. *(Executed for v1.0.0; superseded by CR-17 — future pushes go to GitHub only, and the cgit agefile check no longer applies.)*
4. Place exe on HOME-GAMING-PC; optional Start Menu shortcut. No installer.

---

## 8. Phase 6 — Maintenance & Enhancement Backlog

| ID | Item | Notes |
|---|---|---|
| E-01 | Arabic key labels + layout-aware label switching | **Delivered in v1.1.0** as F-12 dual-script keycaps per Amendment A1 (no layout switching — both scripts always shown) |
| E-02 | Numpad block (toggleable) | Extends KeyMap |
| E-03 | Function-row (F1–F12) toggle | **Delivered in v1.1.0** as F-14 permanent function row per Amendment A1 |
| E-04 | Themes beyond flat colors | Low priority |
| E-05 | Distribution channels (winget / Scoop manifests) | Added by Amendment A2; sensible after v1.2.0 exists |

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
| 2026-07-21 | CR-07 | **WinExe output type** with AttachConsole/AllocConsole plumbing so `--selftest`/`--harness` remain console modes while the keyboard opens no console window. | Approved — Nasser |
| 2026-07-21 | CR-08 | **Harness abort cleanup:** spawned Notepad closed via PID-diff + graceful WM_CLOSE, best-effort (Win11 stub hand-off). Closes the M1 polish item. | Approved — Nasser |
| 2026-07-21 | — | **Gate 2 / M2 approved.** Witnessed: T-01 (10 snips, zero freezes, keyboard absent, toggle reappearance confirmed) and T-02 (20 keys, focus never left target). Tagged `m2-window-shell`. | Approved — Nasser |
| 2026-07-21 | CR-09 | **T-05 amended.** "Win key opens Start" replaced by the chord test "arm Super, press E → Explorer opens." Second press of an armed modifier stays a pure disarm — emitting on cancel would conflate cancel with emit (armed Alt → app menus, armed Win → Start). New limitation L-04 added to §3.3. | Approved — Nasser |
| 2026-07-21 | CR-10 | **`tests/KeyMapTests.cs` ratified** into the §4.1 tree (same class of change as CR-04). | Approved — Nasser |
| 2026-07-21 | — | **Gate 2 / M3 approved.** Witnessed: T-03 (Arabic/EN via D-02), T-05 (sticky Shift, label swap, Ctrl round trip, Super+E chord per CR-09), T-06 (repeat with release and pointer-leave cancel). T-04 deliberately partial until F-11 at M4. Tagged `m3-full-keyboard`. | Approved — Nasser |
| 2026-07-21 | CR-11 | **`src/app.manifest` and `tests/SettingsTests.cs`** added to the §4.1 tree. | Approved — Nasser |
| 2026-07-21 | CR-12 | **Borderless resize** via WM_NCHITTEST edge band through `HwndSource.AddHook` on softkeys' own HWND (in-process subclass; NF-01 compliant). Replaces CanResizeWithGrip. | Approved — Nasser |
| 2026-07-21 | CR-13 | **Maximize fully blocked** (single-click-only DragMove, caption double-click swallowed, OnStateChanged revert). T-04's "full-screen" defined as edge-drag, not maximize; noted under F-11. | Approved — Nasser |
| 2026-07-21 | — | **Gate 2 / M4 approved.** Witnessed: T-07 (10-min soak with Bookmap + MotiveWave + 5 screenshots, zero freezes, topmost held), T-08 (White/0.50, auto dark text), T-04 completed via edge-drag scaling, T-09 (persistence round trip + corrupt-file silent defaults). Tagged `m4-settings-polish`. | Approved — Nasser |
| 2026-07-21 | CR-14 | **Single-file publish fix.** `IncludeNativeLibrariesForSelfExtract=true` in csproj (publish-only effect) so the §7 command yields one exe; WPF native libs self-extract to `%TEMP%` on first run; NF-04 intact. | Approved — Nasser |
| 2026-07-21 | CR-15 | **M6 — Public mirror** added to §5: empty public GitHub repo `NasserAlh/softkeys` created pre-push; second remote `github` (HTTPS — the Approver's GitHub SSH key was not registered at release time; switchable later); `main` + all tags pushed after M5 acceptance. **Amended (M6 completion):** repo licensed **LGPL-2.1**, matching upstream vboard (`reference/vboard.py` is redistributed publicly, so the license text is mandatory); full official text at `/LICENSE`, attribution in `reference/README.md` (mdev588, github.com/mdev588/vboard, archived 2026-03, redistributed unmodified). Public README replaced with the Architect's draft (v1.0.0 hash retained inline per §7). Hostname decision: `git.nasserhub.net` stays in public history — already public-facing, and a rewrite would invalidate the published v1.0.0 hashes. v1.0.0 exe published as a GitHub release asset with hash, SmartScreen note, and vboard credit. | Approved — Nasser |
| 2026-07-21 | — | **Gate 2 / M5 launch check passed.** SHA-256 match (`860fefe9…bc91bd`); published exe verified live: typing, full shifted row, capture toggle, edge resize, maximize block, color/opacity, settings restore. | Approved — Nasser |
| 2026-07-21 | — | **FINAL ACCEPTANCE.** All §2.3 success criteria witnessed: zero freezes across soak and capture tests, screenshot invisibility, typing in daily applications, EN and AR both proven. softkeys v1.0.0 accepted; remaining items live in the §8 backlog. | **Signed off — Nasser** |
| 2026-07-21 | CR-17 | **cgit remote removed; GitHub is the system of record.** `github.com/NasserAlh/softkeys` is the sole remote from this date; NF-07 amended from self-hosted cgit to GitHub. The bare repo on `git.nasserhub.net` is untouched and remains a frozen archive of v1.0.0 (through commit `ed83f02` + all tags); its deletion, if ever, is a separate Approver decision. §7 step 3 marked superseded. (CR-16 remains reserved for Amendment A1 adoption at Gate A0.) | Approved — Nasser |
| 2026-07-21 | CR-16 | **Amendment A1 adopted — Gate A0 approved.** `docs/softkeys-v1.1-amendment.md` in force: F-12 dual-script keycaps, F-13 case-aware labels + CapsLock indicator, F-14 permanent function row; D-09..D-13; T-10..T-13; milestones M7/M8, gates A0–A2. Display-layer only; input path unchanged. E-01/E-03 moved from §8; L-05 added to §3.3. | Approved — Nasser |
| 2026-07-21 | — | **Gate A1 / M7 approved.** D-09 open question settled by witnessed capsprobe: `GetKeyState(VK_CAPITAL)` tracked physical CapsLock toggles live on the NOACTIVATE thread — D-09 confirmed as designed, no fallback needed. Witnessed: T-10 (full Arabic letter row matches keycaps + 5 spot-checks; both glyphs legible at min height), T-11 (all four label states correct with matching typed output; CapsLock highlight working), T-12 (Esc, F5, F2, armed Alt+F4 — *but see DF-01: the M8 launch check of the published exe contradicted the Alt+F4 result; T-12 re-runs after the fix*), T-13 regression green (capture invisibility, focus, auto-repeat, settings round-trip at new heights). Extended selftest 198/198. Tagged `m7-v11-implementation`. | Approved — Nasser |
| 2026-07-21 | DF-01 | **Defect — M8 launch check HOLD (Gate A2).** On the published v1.1.0 exe: armed Alt + F4 does not close a scratch Notepad window. Armed Ctrl + F4 works (closes the tab), so chord injection and F4's scan code are proven — the failure is specific to the Alt modifier path. Not a defect: Esc not exiting F11 browser fullscreen is standard browser behavior. Release halted; no push/tag-move until Alt+F4 re-verified and launch check re-run on a rebuilt exe. | Recorded — Nasser |
| 2026-07-21 | CR-18 | **DF-01 root cause and fix.** Witnessed altprobe: the production one-batch Alt+F4 left Notepad open (DF-01 reproduced); the identical events split across SendInput calls with gaps closed it. Root cause: the zero-gap batch — targets that sample modifier state asynchronously at processing time (Win11 Notepad) see Alt already up. Fix: chord injection splits into modifier-downs → key tap → modifier-ups across three SendInput calls with a 60 ms inter-call gap (the witnessed-proven value; two gaps = 120 ms once per chord, repeats unaffected per F-05). Plain taps stay single-batch. D-03 amended: atomicity per call; gapped chords mimic physical key timing. Selftest asserts the split structure, ordering, and gap bound. T-12 Alt+F4 re-witnessed against Win11 Notepad specifically at the re-run launch check. | Approved — Nasser |
| 2026-07-21 | — | **Gate A2 / M8 launch check passed.** SHA-256 match (`bda5b491…a9dc`); published exe verified live: typing, case labels with CapsLock indicator, dual-script caps, capture toggle, edge resize, settings restore. T-12 re-witnessed: armed Alt+F4 closed a Win11 Notepad window — DF-01 closed by CR-18. Chord feel: the 120 ms per-chord pause is imperceptible; no polish CR. | Approved — Nasser |
| 2026-07-21 | CR-17 | **Addendum.** The frozen cgit archive on the VPS has been deleted by the Approver. GitHub is the sole remote and the canonical history from this date. | Approved — Nasser |
| 2026-07-21 | — | **FINAL ACCEPTANCE v1.1.0.** Amendment A1 delivered: F-12/F-13/F-14 witnessed via T-10–T-13 including the re-witnessed T-12, regression green, input path unchanged except the approved CR-18 chord timing. | **Signed off — Nasser** |
| 2026-07-21 | CR-19 | **Amendment A2 adopted — Gate B0 approved.** `docs/softkeys-v1.2-amendment.md` in force: F-15 application icon, F-16 per-user installer (Inno Setup, no elevation), F-17 clean uninstall (settings kept by default, D-18), F-18 in-place upgrade, F-19 minimize (CR-13 `OnStateChanged` guard narrowed to Maximized-only, D-19), and F-20 snap immunity (added at Gate B0: `WS_MAXIMIZEBOX` cleared per D-20; CR-12 edge-drag resize unaffected). NF-08/NF-09; D-14…D-20; L-07 → §3.3; T-14…T-18 (T-18 extended with edge-drag snap checks); milestones M9/M10, gates B0–B2 (Gate B1 evidence: T-14–T-18 witnessed). Housekeeping folded into M9: gitignored CLAUDE.md refreshed to post-v1.1.0 reality; `docs/screenshot.png` tight-cropped to the keyboard bounds. | Approved — Nasser |
| 2026-07-22 | DF-02 | **Defect report — Gate B1 HOLD (T-15).** Approver screenshot evidence: the uninstall settings prompt appeared to default to Yes, violating D-18 (Enter must never delete settings). **Investigation** (same machine, Inno 6.7.3, production-identical `[Code]` running in a throwaway app's real uninstaller, screenshot + behavioral probes): the prompt renders with **No holding the focus ring**; a posted Enter keystroke activated No — the settings directory survived; Escape is inert under MB_YESNO (no cancel semantics — the dialog stays open, nothing deleted). MB_DEFBUTTON2 is honored in both install and uninstall contexts; `TaskDialogMsgBox` rejects MB_DEFBUTTON2 ("Invalid Buttons"). Probable explanation of the screenshot: Win11 renders two distinct highlights — hover fill on the button under the cursor (Yes) vs the focus ring on the default (No). Disposition pending Approver: withdraw DF-02, or harden Escape via MB_YESNOCANCEL (No remains default, Escape→Cancel→keep; adds a visible third button). | Recorded — pending disposition |
