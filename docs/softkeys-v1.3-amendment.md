# softkeys — SDLC Amendment A3 (v1.2.1)

**Project:** softkeys — Custom On-Screen Keyboard for Windows 11
**Amendment:** A3 — `Del` key
**Baseline:** v1.2.0 (released 2026-07-22, tag `v1.2.0` → `6a09d2c`, records in baseline §9)
**Version target:** v1.2.1 (patch — confirmed at Gate C0; `<Version>` bumped 1.2.0 → 1.2.1 so the installer, exe, and Apps & Features agree per D-17)
**Status:** **Released — v1.2.1** (2026-07-27, Gate C2). Gates C0 (scope), C1 (T-19 witnessed) and C2 (release) approved. Amendment delivered in full.

All baseline and A1/A2 rules remain in force. On adoption, committed as `docs/softkeys-v1.3-amendment.md` and recorded as CR-20 in baseline §9.

> **Numbering note (approval required).** `docs/softkeys-a3-brief-word-prediction.md` reserves the name "A3" for English word prediction, but that document is an unadopted *brief* — it explicitly states "Brief only. Not a spec." Amendment numbers attach on adoption, not on drafting. This amendment is therefore **A3** and the word-prediction brief is **renumbered A4** (its own numbering is resolved when the Architect drafts it). If the Approver prefers to keep the word-prediction brief as A3, this amendment becomes A4 and the brief's internal "Amendment A3" references are left untouched. **Confirm at Gate C0.**

---

## B3.1 Scope

Add one key — `Del` — to the on-screen layout. Nothing else changes.

This is deliberately the smallest possible change to the shipped keyboard: one key definition, one scan-code entry, one amended baseline requirement (F-04's stale "five rows" wording), and the selftest assertions that count and constrain the layout. No change to the input path's *mechanics* (D-02 scan codes, D-03/CR-18 batch structure), no new non-functional requirement, no new settings, no new asset, no installer change.

Chosen placement (**Approver decision, 2026-07-27**): **right end of Row 4, immediately right of `↑`** — placement option A. This mirrors a physical nav cluster, where `Delete` sits directly above the arrow cluster, and it keeps the bottom-right corner (`←` `→` `↓`) uncrowded.

---

## B3.2 The key, exactly

`Del` is a **plain key** (not a modifier, no shift label, no Arabic glyph) — `Delete` is a fixed name under both EN and AR layouts and has no shifted character.

| Property | Value |
|---|---|
| Name (layout + `ScanCodeTable` key) | `Delete` |
| Visible cap label | `Del` |
| Width | 2 star units |
| Position | Row 4, last key (immediately after `Up`) |
| Scan code | `0x53`, **extended = true** (E0 53) |
| `IsModifier` / `ShiftLabel` / `Arabic` | false / null / null |

Implementation shape, consistent with how the existing arrow keys are declared in `KeyMap.Rows`:

```csharp
new KeyDef("Delete", "Del", null, 2, false),
```

and in `ScanCodeTable.Keys`, in the Row 4 block beside `["Up"]`:

```csharp
["Delete"] = new(0x53, true),
```

**The extended flag is load-bearing, not cosmetic.** Scan code `0x53` is ambiguous in PC/AT set 1: the nav-cluster `Delete` key transmits **E0 53**, while the *numpad* `.` key transmits plain **53**. Declaring `0x53` non-extended would inject the numpad decimal point — the keyboard would type `.` instead of deleting. This is the single highest-consequence detail in this amendment and is why the `D-05` extended-key assertion must be extended rather than left alone (B3.3).

Consequence for `D-05`: `Delete` becomes the **only** key in the layout whose scan code is the same byte as a *different* non-extended key (numpad `.`, which softkeys does not expose). The `(code, extended)` uniqueness assertion still holds — `(0x53, true)` is new — but the extended set grows from eight keys to nine.

**Auto-repeat interaction (flagged, needs a decision).** `Delete` is non-modifier, so F-07 auto-repeat applies to it unchanged: hold ≥ 400 ms → repeat at 100 ms. This is exactly how a physical Delete key behaves and is a common way users clear a word, so no exception is proposed — but see R-12 (double-delete risk on a freshly focused window). If the Approver wants `Delete` excluded from auto-repeat, that is a one-line change and must be stated here before implementation.

---

## B3.3 New and amended requirements

| ID | Requirement | Test |
|---|---|---|
| F-21 | **`Del` key.** The layout includes a `Delete` key: plain (non-modifier), 2 star units wide, labelled `Del`, positioned at the right end of Row 4 immediately after `Up`. It emits scan code `0x53` with `KEYEVENTF_EXTENDEDKEY` (E0 53) so it acts as the nav-cluster Delete key under any active layout. Subject to F-07 auto-repeat like every other non-modifier key. | T-04, T-19 |
| F-04 (amended) | **Full layout.** ~~Five~~ **Six** rows: the F-14 function row plus the five vboard.py rows (number, QWERTY top, home, bottom letter, modifier), **plus the `Del` key added by F-21**. Number row, QWERTY rows, modifier row, arrow keys. Proportional key widths (Space widest; Backspace, Enter, Shift wider). Row 4 now spans **32** star units (30 + `Del` 2); the other four main rows remain 30 and the function row remains 27. | T-04 |

No new non-functional requirement. NF-01 (zero cross-process calls), NF-02 (zero network code), NF-03 (`net8.0-windows`, x64), NF-04 (no elevation), D-10 (no polling timers) are all untouched and remain in force — a new key adds no new interaction with any of them.

No new limitation. No entry moves from backlog §8.

---

## B3.4 Design decisions

| ID | Decision | Rationale |
|---|---|---|
| D-21 | `Del` joins Row 4 at its right end, beside `↑`. | Approver's choice (option A). Matches physical nav-cluster geography — Delete above the arrows. Rejected: right end of Row 5 (crowds the bottom-right corner), and shaving other keys to hold every row at 30 units (would change existing key geometry, a far larger layout change than the feature warrants). |
| D-22 | Scan code `0x53` **extended**; `Delete` added to the D-05 extended set. | E0 53 is the nav-cluster Delete. Plain 53 is numpad `.` — a silent wrong-key bug if the flag is omitted. The D-05 assertion is amended to assert the nine-key set, so the flag cannot be lost by a later edit. |
| D-23 | Internal name `Delete`; visible label `Del`. | The name must be `Delete` to match the Windows/VK vocabulary and read correctly in `ScanCodeTable`; the cap label is `Del` because that is what physical keyboards and the Approver's request say, and 3 characters fit a 2-unit cap better than 6. The existing label indirection (`KeyDef.Label`) already separates the two — the arrow keys use the same mechanism for glyphs. |

### Risk register additions

| Risk | Impact | Mitigation |
|---|---|---|
| R-11: `0x53` injected without the extended flag | Key silently types numpad `.` instead of deleting — a data-corrupting wrong-key bug that looks like success | D-22; D-05 assertion asserts the exact nine-key extended set; T-19 witnessed against a text field |
| R-12: Held or double-tapped `Del` deletes more than intended, especially immediately after focus lands in a new window | User data loss | Inherited, not introduced: identical to a physical Delete key and to the existing `Backspace` (F-07 auto-repeat). Deliberate backspace/delete on a stale target is the user's own action; softkeys never emits either key on its own (this remains true after A4 word prediction, which is append-only by the brief's §2). Approver may still choose to exclude `Del` from auto-repeat — see B3.2 |
| R-13: Row 4 (32 units) no longer aligns with the other rows (30 units) | Visual asymmetry in the star-sized grid (D-01) | Accepted by design: each row is already an independent star grid (rows 1–5 are 30, the function row is 27), so a 32-unit row breaks no existing invariant. F-11 proportional scaling is per-row and unaffected. To be confirmed visually in T-19 |

---

## B3.5 Milestones and versioning

| Milestone | Deliverable | Gate evidence |
|---|---|---|
| M11 — `Del` key | `KeyMap.cs` + `ScanCodeTable` + `KeyMapTests`/`InputInjectorTests` updated; selftest green at **208** checks (201 + 7 — the full delta is enumerated below); no change to any other file | Selftest output + witnessed T-19 |
| M12 — v1.2.1 package | `<Version>` bumped 1.2.0 → 1.2.1; `installer\build.ps1` run (NF-08) producing the single-file exe + `softkeys-setup.exe`; hashes recorded below | Hashes verified; packaged-exe selftest green |

Gate sequence: **Gate C0** (this document, scope) → M11 → **Gate C1** (T-19 witnessed + selftest green + T-04 re-check) → optional M12 → **Gate C2** (final acceptance).

**Version target.** `csproj` `<Version>` is the single source of truth (D-17). A one-key addition is a **patch**, so at Gate C0 the Approver approved the bump 1.2.0 → **1.2.1**, keeping exe, Apps & Features, and release notes in agreement. This matters concretely: without the bump the installer would have shipped a `Del`-bearing build reporting `1.2.0` — byte-different from the published v1.2.0 asset but indistinguishable in Apps & Features, and colliding with an already-published release asset whose hash is recorded in README and the release notes (release assets cannot be re-uploaded under an existing name without deleting it).

**Selftest assertion changes (all enumerated; measured 201 → 208 = +7, as built):**

| File | Current | After |
|---|---|---|
| `tests/InputInjectorTests.cs` | `"layout defines 77 keys"` | `78` (count only; assertion count unchanged) |
| `tests/InputInjectorTests.cs` | row-4 literal in `layoutRows` ends `…, "Up"` | add `"Delete"` |
| `tests/InputInjectorTests.cs` | `expectedExtended` = 8 names | `"Delete"` added → 9 names (assertion count unchanged) |
| `tests/InputInjectorTests.cs` | `table contains '<name>'` loop | **+1** — new `Delete` row |
| `tests/InputInjectorTests.cs` | spot-code table (24 entries) | **+1** — `("Delete", 0x53)` |
| `tests/InputInjectorTests.cs` | — | **+1** — new R-11 check: `table["Delete"].Extended` is true |
| `tests/KeyMapTests.cs` | `"77 keys total"` | `78` (count only) |
| `tests/KeyMapTests.cs` | `"row {i} spans 30 star units"` for `i` 1…5 | loop now expects 32 for `i == 4`, 30 otherwise (count unchanged) |
| `tests/KeyMapTests.cs` | width table (12 entries) | **+1** — `("Delete", 2)` |
| `tests/KeyMapTests.cs` | — | **+1** — new lock: Row 4 sequence is exactly `Shift_L, Z…M, ,, ., /, Shift_R, Up, Delete` |
| `tests/KeyMapTests.cs` | — | **+1** — new `"'Delete' cap label is 'Del'"` |
| `tests/KeyMapTests.cs` | — | **+1** — new `"'Delete' is a plain key"` (not modifier, no shift label, no Arabic) |

Measured totals as built: InputInjector 132 → **135**, KeyMap 62 → **66**, Settings **7** (untouched) = **208**.

**Guard proven to fail, not just pass (R-11 evidence).** With `["Delete"] = new(0x53, false)` deliberately substituted, the selftest reported exactly `[FAIL] extended-key set matches D-05` and `[FAIL] 'Delete' carries the E0 extended marker (R-11)`; restoring the extended flag returned 208/208. The flag cannot be lost silently.

Unaffected and must stay green: the `(code, extended)` uniqueness check, `key names unique`, the name-level bijection between `KeyMap` and `ScanCodeTable` (both counts move together), the 21 shift-label pairs, the 34 dual-script keys, and the D-05 extension-flag event assertions.

---

## B3.6 Test plan

| ID | Test | Pass criterion |
|---|---|---|
| T-19 | **`Del` key.** Focus a text field containing `abcde`. Tap `Del` at the caret start; **hold** `Del` ~1 s; arm `Shift` and tap `Del`. Also verify under the Arabic (101) layout, and confirm the cap reads `Del` and inherits the F-09 contrast switch on a light background. | Forward-delete removes the character **to the right** of the caret (never `Backspace` left-delete); holding repeats after ~400 ms then every ~100 ms and stops on release or pointer leave; nothing is typed when `Shift`+`Del` is used (no numpad `.` — the R-11 check); identical behavior in Arabic; label legible. |
| T-04 (re-run) | Resize from minimum to full-screen. | Keys scale proportionally; six-row layout matches amended F-04 including `Del`; Row 4's extra 2 units scale without clipping or overlap at minimum size. |
| T-17 (re-run) | `--selftest` from the published exe. | **208/208**, 0 failed. |

---

## B3.7 Records

| Date | ID | Change | Approved |
|---|---|---|---|
| 2026-07-27 | A3 | Del-key amendment drafted (placement option A; scan code E0 53; internal name `Delete`, label `Del`); word-prediction brief renumbered A4 pending confirmation | **pending Gate C0** |
| 2026-07-27 | CR-20 | **Gate C0 approved — Amendment A3 adopted.** Implementer authorized with "implement". Numbering confirmed: this is A3; the word-prediction brief renumbers to A4. Auto-repeat: `Del` stays under F-07 unchanged (R-12 accepted, not exempted). Versioning deferred to a later decision. | Approved — Nasser |
| 2026-07-27 | A3 | **M11 complete.** F-21 implemented: `KeyMap.cs` Row 4 + `ScanCodeTable` `["Delete"] = new(0x53, true)` + test assertions across two suites. Clean rebuild; selftest **208/208, 0 failed**. R-11 guard negative-tested (deliberately non-extended → exactly the two expected failures; restored → 208/208). | Approved — Nasser |
| 2026-07-27 | A3 | **Gate C1 passed — T-19 witnessed.** The Approver ran the published self-contained build (`1.2.0+5574e80`, sha `6820567f…7c15`) and confirmed the `Del` key works: forward-delete, correct behaviour in live use. This is the R-11 end-to-end check — a missing extended flag would have typed `.` instead of deleting. T-04/T-17 re-runs folded into the M12 artifact checks below. | Approved — Nasser |
| 2026-07-27 | A3 | **Gate C0 follow-up — version bump authorized, M12 authorized.** `<Version>` 1.2.0 → **1.2.1**; `installer\build.ps1` run; artifacts built and hashes recorded in B3.8. Scope initially limited by the Approver to **local commit only**. | Approved — Nasser |
| 2026-07-27 | A3 | **Gate C2 passed — v1.2.1 released.** Approver reported the install worked smoothly, upgrading the installed app to the new version, with the `Del` key functioning — re-witnessing **T-19** and incidentally **T-16** (in-place upgrade, single 1.2.1 Apps & Features entry, settings kept). Tag `v1.2.1` (`c6d8631`) pushed; GitHub Release published with both assets; API-verified digests match (B3.8). | Approved — Nasser |
| 2026-07-27 | A3 | **Final acceptance v1.2.1** — Amendment A3 delivered in full; full record in baseline §9 | **Signed off — Nasser** |

**Implementation deviation recorded at M11 (process note, not scope):** the first build after the test edits reported two spurious failures caused by a stale incremental build, not by the source. A `bin`/`obj` clean rebuild produced 208/208. No code difference resulted; recorded because a stale-build false negative could otherwise be mistaken for a real defect, or worse, mask one. Recommend a clean rebuild before every witnessed gate run.

---

## B3.8 Build verification (M12)

Run 2026-07-27 by the Implementer; artifacts produced by `installer\build.ps1` (the script printed both hashes, independently re-verified here).

| Artifact | Detail | Value |
|---|---|---|
| single-file exe | path | `bin\Release\net8.0-windows\win-x64\publish\softkeys.exe` |
| | size | 154.4 MB (161,918,413 bytes — self-contained, `IncludeNativeLibrariesForSelfExtract` per CR-14) |
| | FileVersion / ProductVersion | `1.2.1.0` / `1.2.1+5574e80bb3f585281f019c3a98b3f9e36f930c99` (the suffix is the stamped source revision, not a hash) |
| | **SHA-256** | `18fba8ec844e0f7fd5d34b866b9af87d69d9f98322bb2cd2790e6654954a9eb3` |
| | selftest | **208/208, 0 failed**, run standalone from the packaged exe (exit 0) — includes all four `Delete` checks |
| installer | path | `installer\output\softkeys-setup.exe` |
| | size | 47.0 MB (49,333,184 bytes); Inno compile 14.8 s |
| | ProductVersion | `1.2.1` (D-17 injection confirmed) |
| | **SHA-256** | `01bba7ad71da7ba5deaa4119401d52bdf9411db7a48c7f5ede6d9eb443f9ccbf` |

The stamped source revision `5574e80` equals `git rev-parse HEAD`, confirming both artifacts came from the committed tree.

**Environment deviation carried forward (unchanged by this amendment, flagged again):** `build.ps1` line 16–17 falls back to the SDK on `PATH` when the documented user-local SDK 8 is absent. On this machine `%LOCALAPPDATA%\Microsoft\dotnet` does not exist, so **SDK 9.0.318** at `C:\Program Files\dotnet` built these artifacts, not SDK 8 (CR-05 / the "SDK 8 on both PCs" decision). The target remains `net8.0-windows`, so the product output is unaffected — but these hashes are not reproducible on an SDK-8 machine. This is the same drift reported in the 2026-07-27 v1.2.0 install audit, still not resolved; it belongs in a CR of its own.

### Release verification (Gate C2, 2026-07-27)

Tag `v1.2.1` at `c6d8631`, pushed to origin; GitHub Release published with both assets.

| Check | Result |
|---|---|
| GitHub-computed asset digest vs local hash — exe | **match** (`18fba8ec…9eb3`, 161,918,413 bytes) |
| GitHub-computed asset digest vs local hash — setup | **match** (`01bba7ad…ccbf`, 49,333,184 bytes) |
| `releases/latest` | resolves to `v1.2.1` |
| Draft / prerelease flags | false / false |
| Installed copy after running setup over v1.2.0 | `1.2.1.0`, byte-identical to the released exe; Apps & Features shows a single `1.2.1` entry — **T-16 re-witnessed** |

The upgrade path is the part worth noting: the Inno `AppId` (`{31641528-C159-4C55-92A6-621AC5B6E0E5}`) is a fixed GUID, so 1.2.1 replaces 1.2.0 in place rather than installing alongside it. Confirmed on the Approver's machine, not inferred.
