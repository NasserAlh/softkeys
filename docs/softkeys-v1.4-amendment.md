# softkeys — SDLC Amendment A4 (v1.3.0)

**Project:** softkeys — Custom On-Screen Keyboard for Windows 11
**Amendment:** A4 — English word prediction (completion only)
**Baseline:** v1.2.1 (released 2026-07-27, tag `v1.2.1` → `c6d8631`, records in baseline §9)
**Version target:** v1.3.0 (minor — a new user-facing feature)
**Status:** **In force — CR-21.** Authored by the Implementer under the Approver's blanket authority of 2026-07-27 ("full authority to execute this feature … based on your assessment"), which stands in place of a separate Gate D0 signature. Implementation is M13; the release gate remains the Approver's.

Inputs: `docs/softkeys-a3-brief-word-prediction.md` (the Approver's brief, renumbered A4) and `docs/softkeys-a4-assessment-word-prediction.md` (the Implementer's assessment, including the measured dictionary and lookup data and the licence findings).

All baseline and A1–A3 rules remain in force. Recorded as CR-21 in baseline §9.

---

## D4.1 Scope

A suggestion strip above the key grid offering up to three completions of the English word currently being typed, and a tap-to-accept action that emits only the missing letters.

**Explicitly excluded, per the brief:** no correction (softkeys never emits Backspace or Delete on its own), no learning (no frequency file, nothing on disk about what the user types; the dictionary is fixed and read-only), no Arabic prediction, no mid-word replacement, no rewriting, no grammar.

The whole design follows from one constraint: **softkeys cannot read the target text field** (NF-01 — that prohibition is why it does not freeze). Prediction therefore works from a *shadow buffer* rebuilt only from softkeys' own emitted characters. Completion-only is what makes a stale buffer survivable: accepting a suggestion can only ever append.

---

## D4.2 New Functional Requirements

| ID | Requirement | Test |
|---|---|---|
| F-22 | **Word suggestions.** While an English word is being typed, a strip below the key grid shows up to **three** completions of the tracked prefix, most frequent first. Suggestions appear once the prefix reaches **two** letters and extend it (never the prefix itself). Tapping one emits exactly the remaining letters as ordinary unmodified taps, and the tapped word becomes the tracked word. The strip is hidden whenever there are no candidates, which is the common case. | T-20, T-21 |
| F-10 (amended) | **Settings persistence** gains `Prediction` (bool, default **true**). A `settings.json` written before A4 has no such key and therefore deserializes to the default — the intended upgrade behaviour — with the usual silent fallback. Default window height rises 380 → **414** and minimum height 260 → **300** to accommodate the strip. | T-20, T-09 regression |

No new non-functional requirement. **NF-01, NF-02, NF-03, NF-04 and D-10 are all preserved** and are the reason for several decisions below.

---

## D4.3 Design decisions

| ID | Decision | Rationale |
|---|---|---|
| D-24 | Minimum prefix **2**, maximum **3** suggestions. | A one-letter prefix produces enormous, low-value ranges (`t` → the/to/that). Three fits the strip without crowding and matches the brief. |
| D-25 | Dictionary held as an **ordinally-sorted `string[]` with a parallel `ushort[]` rank table**; lookup by binary search to the prefix range, then a fixed-size scan for the best-ranked candidates. **Not** a trie, **not** a DAWG, and **not** a frequency-ordered linear scan. | Measured: a sorted array + rank table is ~4 MB and **0.6 µs** per ranked lookup at 80k words, *smaller and faster* than a trie (20 MB, 1.71 µs). A DAWG looks attractive at ~400 KB but **cannot carry per-word ranks** — including a word's own rank in the state signature collapses minimisation from 37,729 nodes to all 198,015, destroying the advantage. Frequency ordering would make rank implicit but compresses ~36% worse. |
| D-26 | **Boundaries are decided by the emitted character, not by the active layout.** A letter (`char.IsAsciiLetter`) extends the tracked word; *any* other character ends it. | softkeys **cannot** detect the active layout without a cross-process call (NF-01) — it injects scan codes precisely so it never has to know. Emitted characters give the answer for free: under Arabic (101) a letter key emits a non-ASCII codepoint, which ends the word, so the strip naturally stays out of the way with no layout detection at all. |
| D-27 | **Backspace and Delete end the tracked word** rather than removing one tracked character. | The caret position is unknown, so "pop one character" would be a guess; a wrong guess risks appending a completion into the middle of a word. Discarding the fragment costs suggestions until the next word boundary and cannot corrupt anything. |
| D-28 | Dictionary is a **Brotli-compressed embedded resource** (`assets/softkeys-words.br`, `LogicalName=softkeys.words`), format `#<count>` then `<word>\t<rank>` per line in ascending ordinal order, rank 0 = most frequent. Loaded once, lazily, on first use. | Measured 70–190 KB compressed for 30k–80k words — ~0.2% of the exe, against the brief's 1–2 MB estimate. Committed so a clone builds with no network and no corpus (precedent: `assets/softkeys.ico`); regenerated by `tools/DictionaryBuilder`. `LogicalName` avoids coupling to RootNamespace vs AssemblyName. A missing or malformed resource yields **no predictor** rather than a failed start. |
| D-29 | All Shift/CapsLock-dependent state is read **before** the injection in `EmitKey`, and the emitted character comes from one shared helper (`EmittedChar`) used by both the predictor and `RefreshKeyCaps`. | The latch release clears armed modifiers before `EmitKey` returns, so a hook placed after the tap would record the wrong case and mishandle shifted symbols. Sharing the helper guarantees keycaps and buffer cannot drift. |
| D-30 | Accepting a suggestion emits through `InputInjector` directly, **bypassing `EmitKey`**. | Reusing `EmitKey` would start the auto-repeat timer and feed the emission back into the predictor that produced the suggestion. |
| D-31 | Dictionary data is **public-domain only**: vocabulary from a permissively-licensed list, frequency ranking computed in-project from Project Gutenberg text. | See D4.4. |

---

## D4.4 Dictionary provenance (the gate-blocking item, resolved)

The assessment found that the three obvious sources cannot be used, and that one of them misstates its own licence. This amendment therefore **does not adopt any third-party frequency data**:

| Source | Why not used |
|---|---|
| `google-10000-english` | LICENSE.md scopes use to "educational and personal/research", directing others to the Linguistic Data Consortium |
| Norvig `count_1w.txt` (and its `count_1w100k.txt`) | Same LDC/Web-Trillion provenance; Norvig's page grants MIT **for the code**, not the data |
| SymSpell `frequency_dictionary_en_82_765.txt` | README claims Google Books Ngram (CC-BY 3.0) + SCOWL, but its counts are **byte-identical to Norvig's** (`the 23135851162`, `of 13151942776`, …), so the stated licence cannot be relied on. Also not reproducible from its stated recipe |
| SUBTLEX, wordfreq/CC-BY-SA | Research-only terms; share-alike on embedded data conflicts with LGPL-2.1 |

**Adopted instead:** vocabulary filtered against a public-domain (Unlicense) word list, ranked by frequency counted over public-domain Project Gutenberg prose. The generator is `tools/DictionaryBuilder/Program.cs` and the full recipe is reproducible offline from committed inputs. Attribution is recorded in the README alongside the existing vboard credit.

**Accepted limitation (L-08):** Gutenberg frequency is *literary* English, so rankings skew toward narrative vocabulary rather than conversational usage. Mitigated by taking the ranking over a broad multi-book corpus rather than a single author, and by the count threshold that removes one-off tokens. Quality is measured and reported, not assumed.

---

## D4.5 Risk register additions

| Risk | Impact | Mitigation |
|---|---|---|
| R-14: stale shadow buffer — the user types physically, clicks elsewhere, or the app changes its own text; the buffer no longer matches reality | Accepting a suggestion appends letters somewhere unintended. Note this is *worse* than a stray keypress: it is a multi-character burst | Completion-only makes the worst case append-only, never a deletion. The typed fragment is visible in the candidate set, so the user is the sync check (the brief's own mitigation). Backspace/Delete end the fragment (D-27). Residual risk accepted and documented |
| R-15: literary frequency skew degrades suggestion quality | Suggestions feel wrong or archaic | Multi-book corpus; count threshold; quality measured and reported (§D4.4). Dictionary is replaceable without code change |
| R-16: window height increase changes an existing user's layout | Silent visual regression on first run after upgrade | Default height raised to 414; minimum 260 → 300; the strip is collapsed whenever there are no candidates, so the common case costs nothing. The Approver's own settings are migrated by the changed default |
| R-17: dictionary blob corrupt or missing in a build | Keyboard still launches but never suggests, silently | Loader returns null and every prediction path is null-guarded; the selftest asserts the shipped resource loads and yields completions, so a broken blob fails the gate rather than shipping |

---

## D4.6 Test plan

| ID | Test | Pass criterion |
|---|---|---|
| T-20 | **Suggestion behavior (headless).** Drive the predictor directly: reset table, ordering, append-only acceptance, malformed-dictionary rejection, and the shipped dictionary's integrity. | All prediction checks green; every suggestion a strict extension of the prefix; `Accept` never returns a negative or empty remainder for a valid candidate |
| T-21 | **Witnessed use.** Type `hel` on the on-screen keyboard into a text field with suggestions on; confirm the strip offers candidates and that tapping one appends only the missing letters. Then verify: tapping again does nothing; Backspace/Delete clear the strip; switching to Arabic produces no suggestions; the strip is absent when nothing matches; toggling "Word suggestions" off hides it and persists across a restart. | All behaviours as specified |

---

## D4.7 Milestones

| Milestone | Deliverable | Gate evidence |
|---|---|---|
| M13 — prediction | `src/Prediction.cs`; emit-path integration (`EmittedChar`, pre-emit state); suggestion strip; settings + height migration; dictionary asset and generator; selftest coverage | Selftest green with prediction checks included; witnessed T-21 |
| M14 — v1.3.0 release | Publish exe; hashes in README; tag; push; GitHub Release | Hashes verified; release live |

Gate sequence: **M13** → **Gate D1** (T-21 witnessed + selftest green) → **M14** → **Gate D2** (release).

---

## D4.8 Records

| Date | ID | Change | Approved |
|---|---|---|---|
| 2026-07-27 | CR-21 | **Amendment A4 adopted; F-22 word prediction authorized for implementation.** Implementer granted blanket authority ("full authority to execute this feature in the most optimized way, based on your assessment"). Design follows the assessment: sorted array + parallel rank table (D-25), boundaries from emitted characters rather than layout (D-26), public-domain dictionary only (D-31/D4.4), accepting suggestions bypasses `EmitKey` (D-30). Version target v1.3.0. | Approved — Nasser |
| 2026-07-27 | A4 | **M13 complete — prediction implemented; selftest 208 → 275, 0 failed.** New `src/Prediction.cs` (shadow buffer, reset table, prefix index, append-only acceptance) and `tests/PredictionTests.cs` (66 checks). Integration: `EmittedChar` shared with `RefreshKeyCaps` and all Shift/CapsLock state read *before* injection (D-29); suggestion strip docked below the key grid with `Focusable=False`; accepting bypasses `EmitKey` (D-30); `Prediction` setting with pre-A4 files defaulting on; window height migration (R-16). Dictionary: **42,611 words, 457,533 B raw → 178,261 B Brotli** with the fixed-width rank table. | Approved — Nasser |
| 2026-07-27 | A4 | **Dictionary rebuilt twice on the quality pass.** (1) **Project Gutenberg boilerplate stripped** — the licence wrapper is 2.8% of characters but pushed `project`, `copyright`, `works`, `terms`, `foundation`, `electronic` into the top 500 ranks, where they surfaced as suggestions. Measured: removing it moves 57% of the shared top-30k words by more than 100 ranks. (2) **Dictionary enlarged 30,000 → 42,611 words** by raising the cap to admit every word with corpus count ≥ 2. The 30k cap was cutting words by a 1–2 occurrence margin, and the casualty list included **`keyboard` itself** (count 3, qualified rank 34,759), plus `phone`, `taxi`, `folder`, `baseball`, `awesome`. The larger list costs +51 KB on a 154 MB exe, which is not a trade worth refusing; `keyb → keyboard` now works. | Approved — Nasser |

### D4.9 Build verification (M13)

| Artifact | Detail | Value |
|---|---|---|
| single-file exe | FileVersion / ProductVersion | `1.3.0.0` / `1.3.0+bf2a927…` |
| | size | 154.6 MB |
| | **SHA-256** | `8812e7610091d5236dea228a6840ddea811422d8a511678bf53b0c25cfc7b228` |
| | selftest (run from the packaged exe) | **275/275, 0 failed** — proves the Brotli dictionary survives single-file publishing and extraction |
| installer | ProductVersion | `1.3.0` |
| | size | 47.2 MB |
| | **SHA-256** | `16b0ca609c87e5ffafb5a0ac1cf18659441cd8275e9ac7fc45b453f63823455f` |
| dictionary blob | size / SHA-256 | 178,261 B / `25ed83b0c4c4c0bccbedf020a5fbc048f461bc4489bd241664277f9efcfa734e` |

**Verification beyond the selftest.** The engine was also driven against the real shipped dictionary: suggestions were printed for 13 realistic prefixes (`th` → the/that/this, `addr` → address/addressed/addressing, `passw` → password), and the append-only invariant was machine-checked over 30 real candidates with **0 violations** (every suggestion a strict extension; `Accept` returning exactly the difference; a second accept emitting nothing). The strip was rendered and visually confirmed showing `help | held | helped` for the prefix `hel`. F-01's capture exclusion was temporarily disabled for that screenshot and **restored afterwards**; the Approver's `settings.json` was backed up and restored byte-for-byte.

**Dictionary source manifest:** 87 Project Gutenberg books, 60,237,501 characters → 10,520,314 tokens after the licence wrapper is removed, vocabulary filtered against the Unlicense `dwyl/english-words` list, count threshold ≥ 2 (42,611 words qualify), emitted in ascending ordinal order with a fixed-width little-endian `uint16` rank table. Corpus and intermediate outputs live in the gitignored `.dictbuild/`; only the packed asset is committed.

### D4.10 Known dictionary limitations (measured, not assumed)

The dictionary is functional but its register is **literary English**, and that shows. These are recorded rather than glossed:

- **Modern vocabulary is thin.** The corpus is 87 pre-1930 novels, so words invented later are absent by construction. Two distinct failure modes: words missing from the vocabulary list entirely, and words present but never occurring in the corpus (`internet`, `browser`, `download`, `software`, `video`, `email`, `phone`). Some are outright absent from the source list, so no tuning recovers them.
- **Archaic and narrative words rank too high.** `awestruck` outranks `awesome` for the prefix `awes`; `photograph` outranks `phone` for `pho`. `said`, `cried`, `replied` appear where `told`/`asked` would in modern prose.
- **Apostrophe forms are structurally impossible.** The pipeline is letters-only, so `don't`, `it's`, `I'll` can never appear; `don`, `ll`, `re` survive as split fragments (ranks 119/164/331) and are useless as completions.
- **One-character words are excluded** by the 2–20 length rule, so `a` and `I` — two of the most-typed tokens in English — are not in the dictionary.
- **Proper nouns leak** because the vocabulary list contains lowercase common nouns that are also names or demonyms (`english`, `paris`, `tom`, `christ`, `hellenic`). `hell → hello, hellenic, hellish` is the visible symptom.

**Assessment:** acceptable as a *literary/formal English* completion list; **not** equivalent to a modern general-purpose dictionary, and the gap cannot be closed from Project Gutenberg plus a public-domain word list. Closing it properly needs a licence-clean modern word source, which does not currently exist under the constraints chosen in D4.4 — that is a known, accepted limitation (L-08), not an oversight. It is recorded in the README's limitations section for users.

**Gate D1 status:** selftest green on the shipped artifacts. The remaining witnessed step is tapping a suggestion with the mouse and confirming the letters land in a focused application (T-21), which the Approver performs on the installed build.
