# softkeys — SDLC Amendment A2 (v1.2.0)

**Project:** softkeys — Custom On-Screen Keyboard for Windows 11
**Amendment:** A2 — Application icon, installer, and uninstaller
**Baseline:** v1.1.0 (released 2026-07-21, tag `v1.1.0` → `583619e`, records `af790b3`)
**Version target:** v1.2.0
**Status:** Adopted (CR-19). M9 implementation complete; Gate B1 in progress — T-14 passed, DF-02 withdrawn, T-15 re-run (Enter-press check) + T-16/T-17/T-18 pending

All baseline and A1 rules remain in force. On adoption, committed as `docs/softkeys-v1.2-amendment.md`, recorded as CR-19 in baseline §9.

---

## B2.1 Scope and technology decision

Deliver an elegant install/uninstall experience and a proper application identity (icon), without compromising the project's principles: no elevation, no network code, no signing requirement, portable exe preserved.

**Installer technology: Inno Setup** (free, mature, scriptable, compiled locally). Decision rationale:

| Option | Verdict |
|---|---|
| Inno Setup | **Chosen.** Per-user installs without elevation, clean Apps & Features integration, optional-components UI, silent-install flags, single setup.exe output, no runtime dependency. |
| MSIX | Rejected: unsigned MSIX cannot be installed without certificate gymnastics on every user's machine — worse than SmartScreen for a free unsigned tool. |
| WiX (MSI) | Rejected: heavyweight authoring for no user-visible gain at this scale. |
| winget / Scoop manifests | Deferred to backlog E-05: distribution channels, not installers; sensible after v1.2.0 exists. |

The portable exe remains a first-class release asset alongside the installer.

---

## B2.2 New Functional Requirements

| ID | Requirement | Test |
|---|---|---|
| F-15 | **Application icon.** A multi-resolution `softkeys.ico` (16/24/32/48/64/128/256 px) embedded in the exe (`<ApplicationIcon>`), shown as the window icon, taskbar icon, Explorer file icon, installer/uninstaller icon, and Apps & Features icon. Icon design: flat keycap motif with dual-script identity (Latin + Arabic glyph), legible at 16 px, working on light and dark backgrounds. Source SVG committed under `assets/`; ICO generated from it by a scripted, repeatable step. | T-14 |
| F-16 | **Per-user installer.** Inno Setup script (`installer/softkeys.iss`) producing `softkeys-setup.exe`: installs to `%LOCALAPPDATA%\Programs\softkeys` with **no elevation prompt** (`PrivilegesRequired=lowest`); Start Menu shortcut always; desktop shortcut optional (unchecked); "Start softkeys when I sign in" optional (unchecked, HKCU Run key); registers in per-user Apps & Features with name, version, publisher, icon; supports `/SILENT` and `/VERYSILENT`; installer makes no network access. | T-14 |
| F-17 | **Clean uninstall.** Uninstaller removes program files, shortcuts, and the Run key if present. Settings (`%APPDATA%\softkeys`) are **kept by default**; the uninstaller asks whether to remove them (explicit opt-in to delete). No files or registry keys left behind other than settings-if-kept. | T-15 |
| F-18 | **In-place upgrade.** Running a newer `softkeys-setup.exe` over an existing install replaces the app cleanly: version updates in Apps & Features, single Apps & Features entry, settings untouched, no duplicate shortcuts. | T-16 |
| F-19 | **Minimize.** A "—" button in the header bar (left of ✕) minimizes the keyboard to the taskbar; clicking the taskbar icon restores it to its previous size/position with all NOACTIVATE/topmost/capture-affinity styles intact and without the keyboard taking or holding keyboard focus. Requires narrowing CR-13's state guard: `OnStateChanged` reverts **Maximized only**; Minimized is permitted. Maximize remains fully blocked. | T-18 |
| F-20 | **Snap immunity.** Dragging the keyboard against any screen edge or corner must not trigger Windows Snap resizing (no snap overlay, no size change); the window simply moves there. Implemented by removing the window's maximize capability at the Win32 level (`WS_MAXIMIZEBOX` cleared / shell snap participation withdrawn) — CR-13 already establishes that softkeys has no valid maximized or snapped state. Edge-drag resize via our own WM_NCHITTEST band (CR-12) remains fully functional. | T-18 |

### Non-functional additions
- NF-08: The installer build is scripted and repeatable (`installer/build.ps1` or equivalent single command); the release process compiles it from the published exe, and both artifacts' SHA-256 hashes are recorded in the README and release notes.
- NF-09: Neither installer nor uninstaller requires or requests elevation at any point (aligns NF-04). If Inno's compiler emits an elevation manifest by default for any stub, it is explicitly overridden.

### Limitations (append to §3.3)
- L-07: `softkeys-setup.exe` is unsigned like the exe itself — SmartScreen will warn. Recorded in README and release notes with the same verify-hash-or-build-from-source guidance.

---

## B2.3 Design decisions

| ID | Decision | Rationale |
|---|---|---|
| D-14 | Install root `%LOCALAPPDATA%\Programs\softkeys` | Windows convention for per-user apps; matches the location already in manual use; zero elevation. |
| D-15 | Icon pipeline: `assets/softkeys.svg` → ICO via scripted conversion (ImageMagick if available, else a committed PowerShell/.NET conversion script). The ICO is committed too, so a clone builds without the toolchain. | Repeatability with no build-time network or tool dependency. |
| D-16 | Autostart via HKCU `Software\Microsoft\Windows\CurrentVersion\Run` (per-user), written only if the user opts in, removed on uninstall. | Simplest correct per-user autostart; no Task Scheduler, no elevation. |
| D-17 | Installer version is injected from the csproj version at build time (single source of truth). | Prevents version drift between exe, Apps & Features, and release notes. |
| D-18 | Uninstall settings prompt defaults to **keep**. | An uninstall is often an upgrade-by-reinstall or a test; destroying user settings must be the explicit choice. |
| D-19 | Minimize implemented as standard taskbar minimize (`WindowState.Minimized` via the — button); CR-13's `OnStateChanged` guard narrowed from "revert any non-Normal state" to "revert Maximized only". Restore path is the user's taskbar click; no tray icon, no custom collapse mode in this version. | Simplest familiar behavior; taskbar restore does not hand the keyboard keyboard-focus (NOACTIVATE persists across minimize/restore). Tray or collapse-to-strip modes go to backlog if ever wanted. |
| D-20 | Snap immunity (F-20) via clearing `WS_MAXIMIZEBOX` on our own HWND at source-init, alongside the existing style work in `WindowStyles`. Without the maximize box the shell excludes the window from Snap (edge/corner snap, Snap Layouts, Win+arrow), so edge drags just move it. No message filtering, no drag-position tracking. | One local style bit on our own window — NF-01-clean, zero runtime cost. Consistent with CR-13: a window with no valid maximized state should not advertise maximize capability to the shell at all. WM_NCHITTEST resize (CR-12) is unaffected — resize capability is `WS_THICKFRAME`, not `WS_MAXIMIZEBOX`. |

### Risk register additions

| Risk | Impact | Mitigation |
|---|---|---|
| R-08: Inno stub or script accidentally requests elevation | Breaks NF-04/NF-09 | `PrivilegesRequired=lowest` asserted; T-14 witnessed on a standard-user session watching for any UAC prompt |
| R-09: Icon illegible at 16 px | Weak identity in taskbar/Explorer | Design constraint in F-15; witnessed check at small sizes in T-14 |
| R-10: Uninstall leaves residue | Trust damage, "elegant" promise broken | T-15 checks files, shortcuts, Run key, and Apps & Features entry post-uninstall |

---

## B2.4 Test plan

| ID | Test | Pass criterion |
|---|---|---|
| T-14 | **Install + identity.** Run `softkeys-setup.exe`: no UAC prompt at any point; install completes to `%LOCALAPPDATA%\Programs\softkeys`; Start Menu entry present; app launches from shortcut; icon correct and legible in window title, taskbar, Explorer, and installer UI (check at small size). Desktop-shortcut and autostart options honored when selected. | All behaviors as specified |
| T-15 | **Uninstall.** Via Apps & Features: entry shows icon + version; uninstall with "keep settings" → program files, shortcuts, Run key gone, `%APPDATA%\softkeys` intact; reinstall, uninstall with "remove settings" → settings folder gone too. No leftovers either way. | Clean both paths |
| T-16 | **Upgrade.** Install over an existing install (same version acceptable as stand-in): single Apps & Features entry, settings preserved, shortcuts not duplicated, app runs. | Clean upgrade |
| T-17 | **Regression.** Portable exe from the same build passes `--selftest` standalone and launches with all v1.1 behavior (spot-check: dual caps, Alt+F4 chord on Win11 Notepad, capture toggle). | Green |
| T-18 | **Minimize/restore + snap immunity.** Click — → keyboard minimizes to taskbar; click taskbar icon → restores to prior size/position; immediately type into an already-focused Notepad via softkeys → focus was never captured; capture toggle state and topmost behavior unchanged after restore; double-click on the drag bar still does nothing (maximize stays blocked). Snap immunity (F-20): drag the window against the left, right, and top screen edges → no snap overlay, no resize — the window simply moves there; after the edge drags, edge-drag resize (CR-12 band) still works. | All behaviors as specified |

---

## B2.5 Milestones

| Milestone | Deliverable | Gate evidence |
|---|---|---|
| M9 — Icon & installer | `assets/` icon source + ICO, embedded in exe; minimize button (F-19) and snap immunity (F-20); `installer/` script + scripted build | Compiled setup exe handed to Approver; T-14–T-18 witnessed |
| M10 — v1.2.0 release | Publish exe + compile installer; SHA-256 of **both** assets in README and notes; tag `v1.2.0`; push; GitHub Release with both assets | Hashes witnessed; release page live |

Gate sequence: **Gate B0** (scope) → M9 → **Gate B1** (T-14–T-18 witnessed) → M10 → **Gate B2** (final v1.2.0 acceptance).

Housekeeping folded into M9 (record as part of CR-19): refresh the gitignored CLAUDE.md to post-v1.1.0 reality (cgit deleted, A1 delivered, current milestone M9); tight-crop `docs/screenshot.png` to the keyboard bounds.

---

## B2.6 Records

| Date | ID | Change | Approved |
|---|---|---|---|
| 2026-07-21 | A2 | v1.2 packaging amendment drafted | pending Gate B0 |
| 2026-07-21 | A2 | **Gate B0 approved** — amendment adopted, recorded as CR-19 in baseline §9. F-20 (snap immunity) + D-20 added at approval; T-18 extended with edge-drag snap checks; Gate B1 evidence is T-14–T-18 | Approved — Nasser |
| 2026-07-22 | A2 | **M9 design review passed.** Icon 256 px dual-glyph design approved; 16/24 px entries use the single bold-S variant (dual glyphs illegible at 16 px — R-09 addressed by per-size art). F-20 confirmed live by the Approver on the dev build: edge drags against left/right/top move the window without snapping; edge-drag resize works before and after. | Approved — Nasser |
| 2026-07-22 | A2 | **M9 implementation complete.** F-15 SVG→ICO pipeline (`assets/generate-icon.ps1`, no external tooling) + icon embedded; F-19/F-20 in build; F-16/F-17/F-18 installer compiled via `installer/build.ps1` (NF-08); setup stub verified asInvoker, no elevation manifest (NF-09). Selftest 201/201. v1.2.0-candidate hashes: exe `c5f6ab7e…d3270`, setup `0e86abc3…63303`. Awaiting witnessed T-14–T-18. | pending Gate B1 |
| 2026-07-22 | A2 | **Gate B1 progress.** T-14 passed (no UAC, install/identity/icon correct; optional tasks unchecked on first run — the second run showed Inno's remembered selections, by design, no DF-03). DF-02 (T-15 settings-prompt default) reported, investigated, and **withdrawn** — reproduction showed No default, Enter keeps settings, D-18 satisfied as built; full record in baseline §9. Remaining: T-15 re-run with a deliberate Enter-press at the prompt, T-16, T-17, T-18; then Gate B1 sign-off and M10. | Approved — Nasser |
