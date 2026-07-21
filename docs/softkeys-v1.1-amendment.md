# softkeys — SDLC Amendment A1 (v1.1.0)

**Project:** softkeys — Custom On-Screen Keyboard for Windows 11
**Amendment:** A1 — Bilingual keycaps, case-aware labels, function row
**Baseline:** v1.0.0 (released 2026-07-21, commit `92174e7`, final records `cd4624c`)
**Version target:** v1.1.0
**Status:** Draft — awaiting Gate A0 approval (amendment scope sign-off)

This amendment extends the baseline SDLC document (`docs/softkeys-sdlc.md`). All baseline rules remain in force: governance model (§1), NF-01 forbidden-API list, evidence-before-mutation, per-gate tags. On adoption, this file is committed as `docs/softkeys-v1.1-amendment.md` and recorded as CR-16 in baseline §9.

---

## A1.1 Scope

Three enhancements, all promoted from or added to the §8 backlog:

| From backlog | Amendment item |
|---|---|
| E-01 (Arabic labels) | F-12 — Dual-script keycaps |
| — (new) | F-13 — Case-aware labels + CapsLock indicator |
| E-03 (F-row) | F-14 — Permanent function row |

**Guiding constraint:** every item in this amendment is display-layer only. The input path (scan-code injection, D-02/D-03) is not modified. NF-01 (zero cross-process calls) remains fully in force — see D-09 for the one clarification this requires.

Out of scope for v1.1: layout auto-detection of the foreground application (rejected by design — it would reintroduce the cross-process query pattern the project exists to avoid), numpad (E-02), themes (E-04).

---

## A1.2 New Functional Requirements

| ID | Requirement | Test |
|---|---|---|
| F-12 | **Dual-script keycaps.** Every key that produces a different character under the standard Arabic (101) layout displays two glyphs: Latin top-left, Arabic bottom-right (e.g. `D` / `ي`). Both are always visible — no switching, no layout detection. Mapping is a static table in KeyMap keyed by scan code, following the Windows Arabic (101) layout. Keys identical in both layouts (digits, F-row, modifiers, navigation) keep a single label. | T-10 |
| F-13 | **Case-aware labels + CapsLock indicator.** Letter keycaps display lowercase by default. They display uppercase while Shift is armed XOR CapsLock is active (Shift + CapsLock together → lowercase, matching real typing output). The CapsLock key shows an armed-style highlight while active, and the state is re-read on window activation-free focus events (timer-free; see D-10). Existing F-06 symbol shift-labels are unchanged. | T-11 |
| F-14 | **Permanent function row.** A sixth row above the number row: `Esc, F1–F12`, always visible, equal widths (Esc may be wider). Standard scan codes (Esc 0x01, F1–F10 0x3B–0x44, F11 0x57, F12 0x58), non-extended. Participates in sticky-modifier chords (e.g. armed Alt + F4). Window minimum height increases accordingly; resize scaling (F-11) applies to six rows. | T-12 |

### New limitation (append to baseline §3.3)
- **L-05:** Arabic keycaps show the base (unshifted) Arabic glyph only. Shifted Arabic characters (diacritics, tatweel, etc.) are produced correctly when typed but are not displayed on the caps — three glyphs per key would harm readability. Revisit only on user demand.

---

## A1.3 Design Decisions (extend baseline §4.2)

| ID | Decision | Rationale |
|---|---|---|
| D-09 | CapsLock state read via `GetKeyState(VK_CAPITAL)` toggle bit. **NF-01 clarification:** `GetKeyState` reads local input-state synchronized to this thread's message queue — it is not a cross-process query, does not block on any other process, and is hereby explicitly permitted. The forbidden list is otherwise unchanged. | Needed for F-13; keeps the freeze-immunity guarantee intact and documented. |
| D-10 | CapsLock state refresh is event-driven only: re-read on our own window messages (e.g. after each injected key, and on WM_ACTIVATEAPP-class notifications of our own window). No polling timer. | F-08's no-timers principle extended to state reads; an OSK that polls is drift back toward the old design. Accepted consequence: if CapsLock is toggled on the physical keyboard while softkeys is idle, the indicator updates on the next interaction. Recorded as acceptable (L-06 if the Approver wants it listed). |
| D-11 | Arabic glyphs render via the default WPF font stack (Segoe UI covers Arabic); FlowDirection stays LeftToRight for the grid — individual TextBlocks render Arabic glyphs correctly without RTL layout changes. | Single-glyph labels need no bidi layout; avoids mirroring the keyboard. |
| D-12 | Dual-label keycap is a two-TextBlock template (Latin top-left, Arabic bottom-right at ~75% size), replacing Button.Content strings for dual keys. Auto text-contrast (F-09) applies to both glyphs. | Minimal change to existing styling; palette/contrast machinery reused. |
| D-13 | F-row uses the same star-sizing system; row heights equalize across six rows. Min window height raised from 220 to a value set at implementation (~260) and recorded in settings defaults. | Consistency with D-01. |

### Risk register additions

| Risk | Impact | Mitigation |
|---|---|---|
| R-05: Arabic mapping errors (wrong glyph on a key) | Misleading caps; trust damage | Mapping table verified by test against the documented Arabic (101) layout; T-10 witnessed spot-checks key-by-key on one full row |
| R-06: Dual labels unreadable at small window sizes | Usability | Arabic glyph scales with key font; witnessed check at minimum window size in T-10 |
| R-07: CapsLock indicator desync (D-10 consequence) | Cosmetic staleness | Re-read on every interaction; documented behavior |

---

## A1.4 Test Plan (extend baseline §6)

| ID | Test | Pass criterion |
|---|---|---|
| T-10 | **Dual-script correctness.** With Windows layout set to Arabic, click every key in one full letter row and compare output to the Arabic glyph printed on that keycap; spot-check 5 keys in other rows. Then check readability at minimum window size. | Every produced character matches its keycap's Arabic glyph; labels legible at min size |
| T-11 | **Case labels.** Default state → keycaps lowercase; arm Shift → uppercase (and F-06 symbols flip); emit → revert. CapsLock on → uppercase labels + CapsLock highlighted; type into Notepad → capitals produced. CapsLock on + Shift armed → lowercase labels, and typed output is lowercase. CapsLock off → all reverts. | Labels and produced characters agree in all four states |
| T-12 | **Function row.** Esc closes a browser dialog; F5 refreshes a browser page; F2 renames a file in Explorer; armed Alt + F4 closes a scratch window. | All four behaviors correct; chords work |
| T-13 | **Regression pass.** Re-run T-01 (capture invisibility, 3 snips), T-02 (focus), T-06 (auto-repeat), T-09 (settings round-trip incl. new min-size) against the v1.1 build. | All pass unchanged |

Self-test additions required: Arabic mapping table integrity (every dual key has a non-empty Arabic glyph; no dual entries for identical-in-both-layouts keys), F-row scan codes, six-row width sums.

---

## A1.5 Milestone & Release Plan

| Milestone | Deliverable | Gate evidence |
|---|---|---|
| M7 — v1.1 implementation | F-12, F-13, F-14 complete; self-test extended and green | Selftest output; T-10–T-13 witnessed by Approver |
| M8 — v1.1.0 release | Publish per baseline §7; SHA-256 in README and release notes; tag `v1.1.0`; push main + tag to `origin` (GitHub — sole remote per baseline CR-17); GitHub Release with exe asset; README feature list and screenshot updated (screenshot now shows dual-script caps — retake by Approver) | Hash match witnessed; release page live; regression T-13 recorded |

Gate sequence: **Gate A0** (this amendment approved) → M7 implementation → **Gate A1** (T-10–T-13 witnessed) → M8 release → **Gate A2** (final v1.1.0 acceptance).

Settings compatibility: `settings.json` schema unchanged except default/min window height; existing files load without migration.

---

## A1.6 Records

On adoption: record CR-16 (this amendment) in baseline §9; move E-01 and E-03 from §8 to "delivered in v1.1"; add L-05 (and L-06 if elected) to §3.3; update status line per CR-06 practice.

| Date | ID | Change | Approved |
|---|---|---|---|
| 2026-07-21 | A1 | v1.1 amendment drafted | pending Gate A0 |
