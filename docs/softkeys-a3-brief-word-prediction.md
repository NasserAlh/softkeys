# softkeys — Design brief for Amendment A3: English word prediction (completion only)

**From:** Nasser (Approver)
**To:** Architect
**Date:** 2026-07-27
**Status:** Brief only. Not a spec. No code may be written from this document. The Architect drafts Amendment A3 with numbered F-/NF-/D-/T- items; the Approver signs; only then does the Implementer start.

---

## 1. What is wanted

A suggestion strip that predicts the English word the user is typing and finishes it on tap.

Example: the user taps `h`, `e`, `l`. The strip shows up to three candidates, such as `help`, `hello`, `held`. The user taps `hello`. softkeys emits only the missing letters: `l`, `o`.

That is the whole feature.

## 2. What is explicitly excluded

- **No correction.** softkeys must never emit Backspace or Delete on its own. Accepting a suggestion is append-only: it only ever adds the remaining letters of the current word. "teh" will never be changed to "the".
- **No learning.** No frequency file, no record of typed words, nothing written to disk about what the user types. The dictionary is fixed and read-only.
- **No Arabic prediction.** English only. When the Arabic script is active, the strip is hidden or empty (Architect's choice).
- **No mid-word replacement, no rewriting, no grammar features.**

The Approver's instruction is exact: "No correction, nothing, only prediction."

## 3. Why completion-only is safe

softkeys cannot read the target app's text field — NF-01 forbids every mechanism for doing so, and that prohibition is the reason softkeys does not freeze. So prediction must work from a **shadow buffer**: softkeys remembers the keys it emitted itself and rebuilds the current word from its own output.

That buffer can go stale. The user may click the mouse elsewhere, type on the physical keyboard, or let the target app change its own text. softkeys cannot see any of that, by design.

Completion-only makes staleness harmless. If the buffer is wrong, the worst case is a suggestion that makes no sense, which the user simply ignores, or a few appended letters the user did not want. Nothing is ever deleted. This is why correction is excluded: correction requires blind backspaces, and blind backspaces on a stale buffer destroy the user's text.

**Mitigation to specify:** the strip should display the tracked prefix (the word softkeys thinks is being typed) so the user can see at a glance whether softkeys is in sync. The user is the sync check.

## 4. Proposed behavior for the Architect to specify

1. **Shadow buffer.** Rebuilt only from softkeys' own emits. Letters extend the current word. Word-boundary emits (Space, Enter, Tab, punctuation) reset it. Backspace emitted by a user tap removes the last tracked character, or resets the buffer if empty/uncertain. Layout switch (English ↔ Arabic) resets it. The Architect should define the full reset table.
2. **Suggestion strip.** One row above the existing key grid, up to three candidates, largest-frequency first among dictionary words starting with the tracked prefix. Fits the star-sized grid (D-01). The window is already `WS_EX_NOACTIVATE`, so tapping a suggestion cannot steal focus from the target app.
3. **Accept action.** Tapping a candidate emits the remaining letters as ordinary taps (all English letters have scan codes; no `KEYEVENTF_UNICODE` needed). Whether a trailing space is emitted after acceptance is an Architect decision — the Approver has no strong preference.
4. **Dictionary.** Embedded English word list with frequency ranks, compiled into the exe as a compressed resource (`System.IO.Compression` is standard library). Roughly 30k–80k words, adding about 1–2 MB. Loaded once at startup into a sorted array with binary-search prefix lookup (or a trie). No file I/O after startup, no downloads ever (NF-02).
5. **Settings.** One boolean in `settings.json`, e.g. `"prediction": true`, with the usual silent-default fallback (F-10). Suggested default: on.
6. **Trigger discipline.** The engine runs synchronously inside the existing emit path — recompute suggestions after each of our own emits. No polling timers (D-10). Lookup at this scale is microseconds, so the input path stays fast (NF-03 territory — the Architect should state the budget).

## 5. Constraints the spec must restate (unchanged)

- **NF-01:** zero cross-process calls. The shadow buffer exists precisely so no foreign process is ever queried. No UI Automation, no TSF, no hooks, no `AttachThreadInput`.
- **NF-02:** zero network code. Dictionary ships inside the exe.
- No NuGet packages; standard library only.
- No polling timers for state (D-10).
- No elevation (NF-04); target stays `net8.0-windows`.
- Window rules unchanged: `WS_EX_NOACTIVATE`, single `HWND_TOPMOST` assertion, maximize blocked, `WS_MAXIMIZEBOX` cleared.
- Existing injection rules unchanged: scan codes with `wVk=0` (D-02), CR-18 gapped batches for chords. Accept-sequences are plain taps and may follow the plain-tap single-batch rule; the Architect confirms.

## 6. Testing the spec should define

- Selftest suites (extending the current 201 checks): dictionary resource loads and decompresses; prefix lookup returns correct, frequency-ordered candidates; remainder computation (`prefix + remainder = candidate`, append-only, never negative); shadow-buffer reset table; strip hidden when Arabic is active; setting toggle honored.
- A witnessed harness scenario: type a prefix into Notepad via softkeys, accept a suggestion, confirm only the remaining letters appear.
- A witnessed desync scenario: make the buffer stale on purpose (click elsewhere, type physically), then confirm softkeys appends at worst and never deletes.

## 7. Governance and sequencing

- This feature requires **Amendment A3** and a **CR** in baseline §9 before any implementation.
- **Gate B1 is still open** (T-15 re-run with the deliberate Enter-press, T-16, T-17, T-18) and **M10 (v1.2.0 release) precedes everything**. A3 work queues behind Gate B1 sign-off and the v1.2.0 release. Nothing here changes that order.
- Scope estimate for planning: comparable to or larger than A1/A2 — new asset pipeline (word list), engine, UI row, settings, and tests; roughly 1,500–2,500 lines plus the dictionary asset.

## 8. Open questions for the Architect

1. Candidate count: three, or configurable?
2. Trailing space after acceptance: yes, no, or setting?
3. Minimum prefix length before the strip shows (suggest 2)?
4. Strip behavior when no candidates match: hide the row or show it empty (row-height stability vs. screen space)?
5. Word list source and license — must be redistributable under or alongside LGPL-2.1, credited like vboard.
