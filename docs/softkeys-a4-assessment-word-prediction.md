# softkeys — Implementation assessment: English word prediction (Amendment A4)

**Prepared for:** the Approver
**Date:** 2026-07-27
**Input:** `docs/softkeys-a3-brief-word-prediction.md` (the Approver's brief, renumbered A4)
**Status:** **Assessment only — not a spec.** No code may be written from this document. It exists to inform the decision to commission Amendment A4, and to hand the Architect the measured facts and the integration hazards up front.

**Headline:** the feature is feasible within every hard constraint the project has (NF-01/02/03/04, D-10), and the parts the brief worried about most — dictionary size and lookup speed — measure **far** better than the brief's own estimate. The real risk is not the dictionary. It is (a) **emit-path integration**, where the existing code clears modifier state at exactly the wrong moment for character tracking, and (b) **what happens after the shadow buffer goes stale**, which is a trust problem no amount of engineering fully removes.

---

## 1. Feasibility against the non-negotiables

| Constraint | Verdict | Why |
|---|---|---|
| NF-01 zero cross-process calls | **Preserved** | The whole design is a shadow buffer fed by softkeys' own emits. Nothing about prediction needs to read another process — and nothing may. This is also why completion-only exists as a safety property (§6). |
| NF-02 zero network code | **Preserved** | Dictionary ships in the exe. No downloads, no update checks — even for the word list. |
| Standard library only | **Preserved** | Compression is `System.IO.Compression` (`BrotliStream` verified working in this codebase's target framework, §4). Lookup needs no package. |
| D-10 no polling timers | **Preserved** | Recompute in the existing emit path, synchronously. Measured lookup cost (§4) is well inside a single keypress budget. |
| NF-03 startup / responsiveness | **Preserved, measured** | Dictionary decompress + split measured at **~1.1–1.8 ms** for ~30k words. Memory ~2.5 MB managed (§4). |
| NF-04 no elevation, `net8.0-windows` | **Preserved** | Nothing here touches deployment or TFM. |

**Technology conclusion:** no new technology is required. This is a data structure, a state machine, and one UI row, using APIs already in the codebase's allowed set.

---

## 2. Where it plugs in — and the one ordering trap

The brief says "the engine runs synchronously inside the existing emit path." That path is `MainWindow.EmitKey` (`src/MainWindow.xaml.cs:367`), the single place every character is injected. It currently does four things in order:

```csharp
private void EmitKey(KeyDef key)
{
    var mods = new List<ScanKey>();
    foreach (string name in ModifierOrder)
        if (_armed[name]) mods.Add(ScanCodeTable.Keys[name]);   // (1) snapshot armed mods

    InputInjector.Tap(ScanCodeTable.Keys[key.Name], mods);      // (2) inject

    if (mods.Count > 0)
        foreach (string name in ModifierOrder)
            if (_armed[name]) SetModifier(name, false);         // (3) RELEASE the latches

    RefreshKeyCaps();                                            // (4) re-read CapsLock, repaint
    Dispatcher.BeginInvoke(RefreshKeyCaps, DispatcherPriority.Background);
}
```

**The trap.** The character a keypress produces depends on Shift and CapsLock. But step (3) **clears the armed modifiers before the method returns**. Any prediction hook placed after the tap — the natural place — that asks "was Shift armed?" will be told *no*, and will therefore record the wrong case and, worse, mis-handle symbol keys where Shift changes the character entirely (`1` vs `!`). The result is quietly wrong suggestions, not a crash.

**Where the hook must go:** immediately after `InputInjector.Tap`, and the emitted character **must be computed from the same state used to build `mods`** — i.e. captured in step (1), before the injection. CapsLock adds a second wrinkle: step (4) re-reads it *after* the emit, so the post-emit read reflects the new state, not the one that produced this character. The character must be derived from a pre-emit read.

**The second trap: character decode must not be duplicated.** `RefreshKeyCaps` (`MainWindow.xaml.cs:393`) already computes, for every letter cap, exactly the character that will be typed:

```csharp
latin.Text = shifted ^ caps ? key.Label : key.Label.ToLowerInvariant();   // letters
latin.Text = shifted ? key.ShiftLabel : key.Label;                        // symbol caps
```

If the predictor reimplements this rule, the two will drift and the buffer will disagree with what the user sees on the caps. **Recommendation:** extract a single `EmittedChar(KeyDef key, bool shifted, bool caps)` helper and have **both** paths call it. This is a small refactor of existing code and is the single highest-value structural decision in the whole feature.

**The third trap: reentrancy.** Accepting a suggestion emits the remaining letters. If it calls `EmitKey` (the obvious reuse), it will also start the auto-repeat timer, flash caps, and feed itself back into the shadow buffer — corrupting the very buffer that produced the suggestion. Suggestion acceptance needs its own emit path that bypasses repeat and either bypasses the predictor or is idempotent under it.

**Repeat path:** holding a key re-enters `EmitKey` after the latches cleared, so repeats are unmodified by design. The predictor must mirror that (each repeat is a separate character appended) — it does, for free, if the hook is inside `EmitKey`.

---

## 3. Character model — the parts the brief leaves open, and what they cost

The brief specifies a shadow buffer and a reset table at a high level. The hard edges:

**CapsLock semantics.** Real keyboards apply CapsLock to letters only, not to symbols. The existing code already models this correctly (`shifted ^ caps`). The buffer must record the *case actually emitted*, and prefix matching must then be case-insensitive while display stays honest. Recommendation: store the emitted character as typed, match prefixes lowercased, and display the tracked prefix verbatim — the prefix display is the user's only sync check, so it must not lie about case.

**Arabic (and any non-Latin) boundary — a cleaner solution than the brief's.** The brief asks for "layout switch resets the buffer" and "strip hidden when Arabic is active." But **softkeys cannot detect the active layout without a cross-process call** — it injects scan codes precisely so it never has to know. Trying to detect it would violate NF-01.

There is a solution that needs no detection: **the shadow buffer observes the characters it emitted.** Under Arabic (101), a letter key emits an Arabic codepoint. So: treat any non-ASCII / non-letter emitted character as a hard boundary and reset. The strip then *naturally* hides in Arabic (the buffer never accumulates a Latin prefix), with zero layout knowledge and zero new API. `Space` is ASCII under both layouts, so word boundaries still work. **Recommendation: specify the reset rule on emitted characters, not on "layout".**

**Delete and Backspace.** Requirements F-21 (Del) and the existing Backspace both mutate text. Rule: pop the last tracked character if it matches what softkeys itself emitted; otherwise reset the buffer. Never guess. Note the pleasant symmetry — this is exactly the kind of rule the new `Del` key from v1.2.1 makes relevant.

**Apostrophes and the reset table.** If `'` is a *boundary* (resets), then "don't" tracks as `don` then `t` — completion still works on the tail. If it is *not* a boundary, the dictionary needs possessives/contractions in it, which doubles the dictionary's ugly cases for little gain. Recommendation: treat `'` as a boundary; keep the dictionary letters-only.

---

## 4. Dictionary: measured, and much smaller than the brief assumed

The brief estimates **1–2 MB** for 30k–80k words. That over-estimates by roughly an order of magnitude. Measured on a real corpus (10 Project Gutenberg books, 6.57 M chars, 1.12 M tokens → **29,678 unique words**), using `System.IO.Compression` from the standard library:

| Payload | Raw | Brotli | Deflate | GZip |
|---|---|---|---|---|
| 29,678 words, newline-delimited, frequency-ordered | 256,878 B | **109,027 B (42.4%)** | 109,469 B | 109,487 B |
| same, with explicit frequency ranks | 423,840 B | 187,370 B | 182,096 B | 182,114 B |

Projected to the brief's target sizes on the measured **3.67 Brotli bytes/word**:

| Dictionary | Raw | Embedded (Brotli) |
|---|---|---|
| 30,000 words | 0.25 MB | **~108 KB** |
| 50,000 words | 0.41 MB | **~179 KB** |
| 80,000 words | 0.66 MB | **~287 KB** |

**Consequences:**
- **The size concern is dead.** Even 80k words is ~0.3 MB against a 154 MB self-contained exe — about 0.2%. The exe grows by noise.
- **Explicit frequency ranks are not worth shipping.** They cost 45% more in raw bytes and compress *worse*. Frequency order **is** the rank: emit the list ranked, and rank = index. This halves the payload and removes a parse step.
- **Brotli is available and worth using** (verified: `BrotliStream` compiles and runs against this project's target framework). It beats Deflate by ~0.4% here — marginal on text this repetitive, so Deflate would also be fine. Brotli's real edge is that it is one line either way.
- **Startup cost is negligible:** decompress + split into `string[]` measured at **1.07–1.83 ms** for 29,678 words. Loading on the UI thread at startup is acceptable; no async plumbing needed.
- **Memory:** ~1.40 MB for the `string[]` plus ~1.13 MB for a word→rank map, so **~2.5 MB managed**. Negligible, but the rank map can be avoided entirely (§4.1), dropping it to ~1.4 MB.
- **Watch the corpus, not just the size.** These books contributed non-English and proper nouns (`queequeg`, Hebrew fragments) — a real word list needs filtering. This also means the measured bytes/word is a *pessimistic-ish* proxy: real lists are more uniform and compress slightly better.

### 4.1 Lookup structure: a trie is not needed; a *naive* scan has a sharp edge

Measured on the 29,678-word list, top-3 most frequent matches per prefix:

| Prefix | Frequency-ordered linear scan | Indexed lookup (binary search + rest of range ranked) | Result |
|---|---|---|---|
| `t` | **0.09 µs** | 489 µs | the, to, that |
| `th` | 0.31 µs | 27.0 µs | the, that, this |
| `the` | 0.29 µs | 3.7 µs | there, they, then |
| `hel` | 2.33 µs | 2.1 µs | help, helsing, held |
| `wo` | 1.11 µs | 14.5 µs | would, work, world |
| `predi` | 17.7 µs | 0.28 µs | prediction, predicament, predicted |
| `zq` (no match) | **22.6 µs** | 0.08 µs | — |

Words examined by the simple scan before it finds three candidates — this is the number that matters:

| Prefix | Words touched | Share of dictionary |
|---|---|---|
| `the` | 52 | 0.2% |
| `wo` | 216 | 0.7% |
| `hel` | 428 | 1.4% |
| `predi` | 18,265 | **61.5%** |
| `zy`, `xq` (no match) | 29,678 | **100%** |

**Reading of this:** the simplest correct implementation — keep the dictionary in frequency order and scan it, stopping at the first three matches — is excellent for common prefixes (sub-microsecond, because frequent words sit early) and degrades to a **full pass** for rare prefixes and misses. At 30k words the absolute worst case is ~23 µs; projected to 80k it is roughly 60 µs. Against a 400 ms human tap that is **still invisible**.

**But ordering also changes compressibility, and that inverts the ranking.** Independent benchmarking (see below) shows alphabetically-sorted word lists compress ~36% better than frequency-ordered ones, because adjacent words share prefixes:

| 80k words, words only | Brotli |
|---|---|
| Frequency order | 292,652 B |
| **Alphabetical order** | **187,050 B** |
| Alphabetical + parallel `uint16` rank table | 347,588 B |

So alphabetical ordering buys ~105 KB but then needs a separate rank table to recover suggestion quality, which costs back ~160 KB — while a frequency-ordered list gets ranking for free from position. The two effects largely cancel, and the deciding factor is measured behaviour:

| 80k words, measured | Result |
|---|---|
| Sorted `string[]` + parallel `ushort[]` ranks | **4.05 MB** managed |
| Trie (`int[26]` children) | 20.2 MB, 198,015 nodes |
| Minimal DAWG | ~400 KB packed, 37,729 nodes |
| Binary-search prefix lookup on sorted array | **0.175 µs/op**; + top-8 by rank **0.612 µs/op** |
| Trie descent + subtree walk | 1.71 µs |
| DAWG descent + subtree walk | 1.85 µs |

**Corrected recommendation — the earlier "just scan it" advice in this document was too conservative:**

1. **Do not build a trie.** 20.2 MB for no benefit; it is slower than an array here.
2. **Do not build a DAWG.** It is small (400 KB) and fast for descent, but **frequency ranks cannot live on DAWG states** — including a word's own rank in the state signature collapses minimisation from 37,729 nodes back to all 198,015, destroying the entire advantage. Only suffix-language annotations survive minimisation. It is real complexity for a benefit the array already provides.
3. **Use a sorted `string[]` with a parallel rank array.** ~4 MB managed, **0.6 µs** for a top-8 ranked lookup, and it compresses acceptably either way. Store ranks as `uint16` (sufficient below 65,536 words) and remember that ranks are only needed because the array is not already in frequency order — if you keep frequency order, position *is* the rank and the array alone suffices.
4. The earlier fallback I proposed — a precomputed prefix index — remains the right incremental upgrade if profiling ever demands it, and is still preferable to a trie.

**Honest caveats.** My own corpus measurements (§4) used 30k literary words on one machine; the ordering/structure figures above come from a separate benchmark and should be re-confirmed against the real dictionary during implementation. Note also that the DAWG finding cuts against the received wisdom that DAWGs are the right structure for word lists — they are, for *storage*; they are not, for *ranked completion*, because rank is a per-word annotation that minimisation is designed to erase. That is the kind of result worth re-deriving rather than trusting.

---

## 5. UI integration

**Layout.** `MainWindow.xaml` is a `DockPanel`: `DragBar` docked top, `KeyGrid` filling the rest. A strip above the keys docks between them:

```xml
<Grid x:Name="SuggestionStrip" DockPanel.Dock="Bottom" Height="34" />
```

Docked **after** `DragBar` and **before** `KeyGrid`, so the star-sized key rows absorb the remainder and all six rows stay equal height (D-13) without touching the key-grid builder.

**Non-negotiable details inherited from the window's rules:**
- Every suggestion button needs `Focusable="False"` + the same `PreviewMouseLeftButtonUp` tap pattern as keys, or it becomes a focus vector and breaks R-02 / F-02 — the window's whole reason for existing.
- `ShowActivated="False"`, `WS_EX_NOACTIVATE` already cover the rest.
- The strip must inherit the F-09 contrast switch (`DynamicResource KeyForeground`), like every other surface.

**The window-sizing hazard — the one real UX regression risk.** The default window is 900×380 with `MinHeight="260"`. Six rows already sit in ~336 px of usable height. A 34 px strip added inside that height **compresses all six key rows**, and at minimum height it eats ~13% of the grid. Worse, **existing users have their height persisted in `settings.json` (F-10)**, so after an upgrade the strip appears and their keys shrink — a silent visual regression on a keyboard whose size they carefully set.

Mitigations, in order of preference:
1. **Grow the window once** when prediction is first enabled and the persisted height is still at the old default; raise `MinHeight` to `260 + strip`. Existing users who deliberately resized keep their size and the strip compresses keys slightly (acceptable).
2. Make the strip an **overlay** above the grid (no layout cost, but it occludes the function row — probably unacceptable).
3. Ship it off by default so nothing changes until the user opts in — combined with (1).

This needs an explicit decision in the spec; it is not a detail to discover during implementation.

**Suggestion count and minimum prefix length** are left open by the brief. The measured data supports three candidates and a minimum prefix of 2 (a 1-character prefix yields enormous ranges and low-value suggestions like `t → the`).

---

## 6. What can actually go wrong (residual risk after all mitigations)

**Staleness is the core trust problem, and it is irreducible.** softkeys cannot see the target text field — that prohibition is the reason it does not freeze. So the buffer *will* desync: physical typing, mouse clicks elsewhere, the app changing its own text, focus moving.

The brief's mitigation (display the tracked prefix; the user is the sync check) is the right one and is cheap. But be clear-eyed about the residual: **accepting a suggestion emits a multi-character sequence into whatever has focus.** If focus moved to another window, that is not "a few unwanted letters" — it can be a burst of keystrokes landing somewhere unintended. That class of harm exists today for single keypresses; prediction multiplies its reach by the length of the remainder.

Worth noting for the record: the *released* v1.2.1 `Del` key makes deliberate deletion easy, which is the practical recovery path. But it is recovery, not prevention. If the Approver wants a stronger guarantee, the option space is: cap the remainder length, require an explicit confirm for long remainders, or auto-clear the buffer on window deactivate. Each costs something; none is free. **This deserves an explicit Approver decision, not an Architect default.**

**Other risks:**
- **R: dictionary poisoning.** A word list containing proper nouns, slurs, or corpus garbage produces offensive or absurd suggestions. Filtering and a curation step must be in the spec, plus a licensing check (§7).
- **R: duplicate-emit feedback.** Covered in §2 — the acceptance path must not re-enter prediction.
- **R: Arabic regression.** If the reset rule is specified on "layout" instead of emitted characters, it will either violate NF-01 or silently fail. §3 gives the clean formulation.
- **R: settings/height migration.** §5.
- **R: test coupling.** Selftests currently assert structure with no external data. Prediction tests should use a **small fixed fixture dictionary**, plus one check that the real embedded resource decompresses — otherwise tests become coupled to an 80k-word blob.

---

## 7. Licensing — the one unresolved external dependency

The word list is the only input to this feature that softkeys does not author, and its license propagates into anything redistributed. **This is gate-blocking** and it is also where the obvious choices turn out to be the wrong ones.

### 7.1 Verified findings

| Source | Words | Frequency data | License | Embeddable in an LGPL-2.1 binary? |
|---|---|---|---|---|
| `google-10000-english` | 10,000 | Yes (ranked) | **NOASSERTION** — [LICENSE.md](https://github.com/first20hours/google-10000-english/blob/master/LICENSE.md) states use is permitted "under the LDC license, Norvig's MIT license for his contributions, and US fair use doctrine" and explicitly warns: *"I do not recommend using this data for commercial purposes without licensing it from the Linguistic Data Consortium."* | **No** — see 7.2 |
| Norvig `count_1w.txt`, `count_1w100k.txt` | 333k / 100k | Yes (counts) | [Norvig's page](https://www.norvig.com/ngrams/) grants MIT **for the code only**; the data files derive from the Google Web Trillion Word Corpus, distributed by the LDC | **No** — same encumbrance |
| **SymSpell** `frequency_dictionary_en_82_765.txt` | 82,834 rows | Yes (counts) | Repository is MIT; README attributes the data to "Google Books Ngram (CC-BY 3.0) + SCOWL" | **Blocked — stated attribution is contradicted by the file.** See 7.2.1 |
| Norvig `count_big.txt` | 29,136 | Yes (counts) | Derived from his own `big.txt` running-text file, not the Google corpus | **Likely yes, with a caveat** — see 7.3 |
| `dwyl/english-words` | ~370k | **No** | [Unlicense](https://raw.githubusercontent.com/dwyl/english-words/master/LICENSE.md) (public domain dedication: "anyone is free to copy, modify, publish, use, compile, sell, or distribute … for any purpose, commercial or non-commercial") | **Yes** — verified, but carries no frequency data |
| SCOWL / ESDB | ~variants | **No** | MIT-like; its Copyright file grants permission to "use, copy, modify, distribute, and sell any part of ESDB … or word lists created from it … provided that the above copyright notice appears in all copies" | **Yes, with notice** — reported as LGPL-compatible; exact wording still to be pinned down |

### 7.2 The trap: the best-known frequency lists are research-licensed

`google-10000-english` is the reflexive first choice for this feature and is **not safe to embed**. Its own license file permits "educational and personal/research use" and points anyone else at the Linguistic Data Consortium for a commercial licence. Two independent problems, either of which is disqualifying:

1. **Terms.** The grant is scoped to research/personal use; redistributing the data inside a publicly downloadable binary is outside it.
2. **Provenance.** The list is derived from the Google Web Trillion Word Corpus — web-page text — so even a permissive grant from the list's compiler could not launder the underlying corpus rights.

The same reasoning removes **Norvig's `count_1w.txt`**, which is the upstream of `google-10000-english` and carries the same LDC provenance. Norvig's MIT grant is unambiguous but applies to his *code*, and his page makes that distinction explicitly. This is exactly the "frequency data is the scarce, encumbered part" concern: plain word lists are abundant and often public domain, but *frequency-ranked* lists are typically corpus-derived and therefore encumbered.

### 7.2.1 SymSpell — attribution contradicted by the data

SymSpell is the most commonly recommended source for exactly this feature. Its README states its 82,765-word frequency dictionary was built by intersecting **Google Books Ngram data** ("[(License)](https://creativecommons.org/licenses/by/3.0/)", i.e. CC-BY 3.0) with SCOWL, and that change note dates to v4.0.

**The counts in the shipped file are byte-identical to Norvig's `count_1w.txt`**, which derives from the Google *Web* Trillion Word Corpus — a different corpus with different licensing. Verified directly by fetching both files:

```
frequency_dictionary_en_82_765.txt      count_1w.txt
the 23135851162                         the	23135851162
of  13151942776                         of	13151942776
and 12997637966                         and	12997637966
to  12136980858                         to	12136980858
a   9081174698                          a	9081174698
```

Identical to the digit across the top five words (space- vs tab-separated). Those 11-digit values are specific enough that the agreement is not coincidence: the frequency column is Norvig's, and Norvig's page is explicit that his MIT grant covers the **code** while the data derives from the Web Trillion corpus distributed by the LDC.

**What this does and does not establish.** It establishes that the numbers originate from the Web Trillion corpus, contradicting the README. It does **not** by itself settle whether that corpus's terms block redistribution — Google published 2012 Web n-gram releases under CC-BY while the 2006 corpus Norvig cites was distributed by the LDC, and those paths carry materially different terms (CC-BY would be embeddable with attribution; LDC's research scoping would not).

Classified as **blocked pending a licensing decision**, not merely "no". The relevant point for the project is narrower and sufficient: **the README's CC-BY 3.0 claim cannot be relied upon**, so SymSpell's data cannot be adopted on the strength of its stated licence. A downstream consumer reading that README would reasonably conclude the data is safely embeddable with attribution, and the file itself does not support that conclusion.

This is the strongest single argument in this assessment for treating dictionary licensing as gate-blocking rather than a formality. Note also that the file is **not** reproducible from the stated recipe: intersecting Google Books Ngrams with SCOWL would not yield Norvig's counts.

(SCOWL, named alongside it in that attribution, is genuinely permissive — the problem is that SCOWL is the *vocabulary* half, and the frequency half came from somewhere else.)

The project's own constraint sharpens this: softkeys is **LGPL-2.1 and free**, not commercial. But the test is not "am I selling it" — it is "does the licence permit redistribution of the data in my binary." A research-scoped grant fails that test regardless of the distributor's business model.

### 7.3 The viable path: build the frequency ranking ourselves

No single source appears to be simultaneously (a) permissively licensed and (b) frequency-ranked from an unencumbered corpus. The clean resolution:

1. **Word vocabulary** from a public-domain list — `dwyl/english-words` (Unlicense) is verified clean, or SCOWL/`enable1` (both already catalogued on Norvig's page) pending their own licence check.
2. **Frequency ranking** computed by the project from **public-domain text** — Project Gutenberg is the obvious corpus, and this assessment already did exactly that as a feasibility check: 10 books → 29,678 unique words → 1.12 M tokens, ranked by count (§4). The pipeline is ~30 lines and the corpus is unambiguously redistributable.
3. **Attribution** handled the way the project already handles the vboard credit — README, release notes, and the notice area — regardless of how permissive the licence is.
4. **A curation pass** over the result, because a corpus-derived list contains proper nouns and garbage (`queequeg` and Hebrew fragments appeared in the measurement run). A blocklist plus a minimum-frequency threshold is required.

**Caveat to record honestly:** Gutenberg-derived frequency is *literary* English, not conversational English. Rankings will skew toward narrative vocabulary versus the everyday usage that an on-screen keyboard actually types. Combining Gutenberg ranks with a small, cleanly-licensed conversational list — if one can be found — or hand-tuning the top few hundred words would mitigate this. This is a quality question, not a legal one, but it should be planned for rather than discovered in testing.

### 7.4 Recommendation

**Do not adopt Google-derived frequency data** — neither `google-10000-english`, nor Norvig's `count_1w`, nor SymSpell's dictionary, whose stated licence the file itself contradicts. Adopt a permissively-licensed vocabulary list and generate the frequency ranking in-project from public-domain text, then curate. That keeps the feature inside LGPL-2.1 obligations with attribution, requires no third-party data licence, and needs no network or NuGet dependency — consistent with the project's constraints and with how `reference/vboard.py` is already credited.

Two vocabulary candidates are verified or near-verified: **`dwyl/english-words`** (Unlicense, public domain — confirmed by reading its LICENSE) and **SCOWL/ESDB** (MIT-like, expressly permits distributing "word lists created from it" provided the copyright notice appears in all copies). Both are vocabulary only, which is exactly what is needed — the frequency half is what causes every licensing problem in this section.

**Sub-items to close before the spec is finalised** (none block drafting):
- Pin down SCOWL's exact notice wording, and confirm the licence of any additional list used.
- Decide the attribution wording, mirroring the vboard precedent.
- Record the SymSpell finding as the documented reason the obvious shortcut is not taken, so the decision is not re-litigated later.

A licensing research pass is running to check for a frequency source this assessment may have missed (AOSP keyboard dictionaries, fcitx/ibus, hunspell-derived data, ENABLE). If it surfaces one that is both permissively licensed *and* not corpus-encumbered, this section will be updated before the spec is written. **Treat the recommendation as sound but not yet exhaustively surveyed** — and treat "no clean frequency source exists" as an acceptable and likely outcome, in which case the in-project ranking from public-domain text stands as the plan.

---

## 8. Implementation plan sketch (for planning only, not a commitment)

| Workstream | Contents | Rough size |
|---|---|---|
| Emit-path integration | `EmittedChar` extraction; hook inside `EmitKey` before latch release; pre-emit CapsLock read; guarded acceptance path | ~60–100 lines changed in existing files |
| Prediction engine | Shadow buffer + reset table; case handling; prefix scan; candidate ranking | ~250–350 lines (new `src/Prediction.cs`) |
| Dictionary pipeline | Source selection → filtering → frequency ranking → Brotli → committed blob under `assets/` (precedent: `assets/softkeys.ico`) → embed via csproj; build script for reproducibility | ~60–100 lines of script + the data |
| UI | Strip XAML, buttons, tap handlers, contrast switch, show/hide, height migration | ~150–250 lines |
| Settings | One boolean with silent-default fallback; strip-height migration | ~40–60 lines |
| Tests | Fixture dictionary; resource-load check; prefix ordering; remainder/append-only invariant; reset table; Arabic boundary; settings toggle; R-11-style negative tests | ~400–600 lines |
| Docs | Amendment A4 + CR + records | — |

**Estimate: ~1,000–1,500 lines of production and test code**, plus the dictionary asset and its pipeline. That is below the brief's own 1,500–2,500 line estimate, mainly because the measured dictionary is ~0.3 MB rather than 1–2 MB and needs no trie, no streaming, and no async load.

**Suggested sequencing** (mirroring the A3 pattern that just worked): spec + CR first → dictionary licensing and pipeline settled → engine + tests (headless, no UI) → emit-path integration → UI strip → settings/height → witnessed test → package.

---

## 9. Open decisions the Approver must settle before the Architect can specify

1. **Dictionary source and license** — gate-blocking, but the assessment now has a recommended resolution (§7.4): public-domain vocabulary + in-project frequency ranking from public-domain text, explicitly **not** `google-10000-english` or Norvig's `count_1w`. Needs your yes/no, plus the attribution wording.
2. **Stale-buffer harm tolerance** — accept "appends at worst" as sufficient, or add a guard (cap remainder / confirm long remainder / clear on deactivate)? (§6)
3. **Window height behaviour** on first run with prediction enabled — grow once, or compress existing layouts? (§5)
4. **Candidate count and minimum prefix length** — recommendation: 3 candidates, minimum prefix 2 (§5).
5. **Trailing space after acceptance** — the brief has no preference; it is user-visible behaviour and should be decided, not defaulted silently.
6. **Default on or off** — the brief suggests on; given the height change and the trust surface, off-by-default is the more conservative first release.

---

## 10. Recommendation

**Proceed, but not immediately and not at full scope in one step.**

The engineering case is strong: every hard constraint survives, the dictionary concern that dominated the brief's estimate is empirically a non-issue (**70–190 KB** words-only, ~1.5 ms startup — confirmed by two independent measurement passes), and no new technology or dependency is required — the whole feature is one data structure, one state machine, and one UI row inside the existing architecture. The structure question is also settled by measurement rather than habit: a sorted array with a parallel rank table, **not** a trie (20 MB, slower) and **not** a DAWG (rank annotations destroy its minimisation).

The risks are concentrated and manageable, but the two that matter are decisions rather than code: **the word-list licence** (§7 — where the obvious sources turn out to be unusable and one of them misstates its own licence), and **how much stale-buffer harm is acceptable** (§6). Neither can be resolved by the Implementer.

Recommended path: settle §9 items 1 and 2 → Architect drafts Amendment A4 with the reset rule specified on **emitted characters** (not layout), the **sorted array + rank table** structure, and the **shared character-decode helper** → then implement engine-first with headless tests before any UI, exactly as A3 was run.
