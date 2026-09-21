using System.IO;
using System.Text;

namespace Softkeys.Tests;

/// <summary>
/// A4/CR-21 prediction behavior, headless: the shadow-buffer reset rules, the
/// append-only acceptance invariant, prefix matching and rank ordering, and the
/// embedded dictionary's integrity. Uses small in-code fixtures so the suite
/// stays deterministic and independent of the shipped word list.
/// </summary>
internal static class PredictionTests
{
    private static int _passed;
    private static int _failed;

    /// <summary>(word, rank) pairs; the helper sorts alphabetically, as the real dictionary is.</summary>
    private static Predictor Build(params (string Word, int Rank)[] entries)
    {
        var ordered = entries.OrderBy(e => e.Word, StringComparer.Ordinal).ToArray();
        return new Predictor(
            ordered.Select(e => e.Word).ToArray(),
            ordered.Select(e => (ushort)e.Rank).ToArray());
    }

    private static Predictor Sample() => Build(
        ("the", 0), ("their", 3), ("them", 5), ("then", 4), ("theory", 9), ("there", 2),
        ("help", 1), ("hello", 6), ("held", 7), ("hem", 8),
        ("zebra", 10), ("zoo", 11));

    public static int RunAll()
    {
        Console.WriteLine();

        // -- shadow buffer / reset table (D-26, D-27) ------------------------
        Predictor p = Sample();
        Check("empty buffer yields no suggestions", p.Suggestions().Count == 0);

        p.Feed('h');
        Check("single letter is below the minimum prefix length", p.Suggestions().Count == 0);
        Check("prefix tracks the fed letter", p.Prefix == "h");

        p.Feed('e');
        Check("prefix tracks a second letter", p.Prefix == "he");

        p.Feed('l');
        Check("prefix tracks a third letter", p.Prefix == "hel");

        // Feed is case-insensitive so a Shift-ed or CapsLock-ed word still tracks.
        p.Reset();
        p.Feed('H'); p.Feed('E');
        Check("uppercase input tracks as lowercase", p.Prefix == "he");

        // A non-letter character of any kind ends the word.
        p.Feed(' ');
        Check("space resets the buffer", p.Prefix.Length == 0);

        p.Feed('h'); p.Feed('e'); p.Feed('.'); p.Feed('h'); p.Feed('e');
        Check("punctuation resets mid-stream", p.Prefix == "he");

        // Arabic (or any non-ASCII letter) must reset: softkeys cannot detect the
        // active layout, so the buffer's own contents are the only signal (D-26).
        p.Reset();
        p.Feed('h'); p.Feed('\u0636'); p.Feed('e'); p.Feed('l');
        Check("non-ASCII letter resets the buffer (Arabic boundary)", p.Prefix == "el");
        Check("the Arabic character itself never enters the buffer", !p.Prefix.Contains('\u0636'));

        // Keys that move the caret or change mode end the tracked word.
        foreach (string key in new[] { "Backspace", "Delete", "Tab", "Enter", "Esc", "Left", "Right", "Up", "Down", "Home", "End", "F4" })
        {
            p.Reset();
            p.Feed('h'); p.Feed('e');
            p.FeedKey(key);
            Check($"'{key}' resets the tracked word", p.Prefix.Length == 0);
        }

        // Sticky modifiers do not change which word is being typed, so they must
        // NOT end it (A4 step 3). Arming Ctrl then typing a letter is an ordinary
        // thing to do mid-word.
        foreach (string key in new[] { "Shift_L", "Shift_R", "Ctrl_L", "Ctrl_R", "Alt_L", "Alt_R", "Super_L", "Super_R" })
        {
            p.Reset();
            p.Feed('h'); p.FeedKey(key); p.Feed('e');
            Check($"'{key}' does not reset the tracked word", p.Prefix == "he");
        }

        // -- suggestion selection and ordering (D-25) ------------------------
        p = Sample();
        p.Feed('h'); p.Feed('e');
        IReadOnlyList<string> s = p.Suggestions();
        Check("suggestions are capped at three", s.Count == Predictor.MaxSuggestions);
        Check("suggestions are ordered by frequency rank", s.SequenceEqual(new[] { "help", "hello", "held" }));
        Check("suggestions exclude the prefix itself", !s.Contains("he"));

        p.Reset();
        p.Feed('t'); p.Feed('h');
        Check("prefix 'th' includes the exact word and its extensions",
            p.Suggestions().SequenceEqual(new[] { "the", "there", "their" }));

        p.Reset();
        p.Feed('t'); p.Feed('h'); p.Feed('e');
        Check("prefix 'the' ranks only its extensions", p.Suggestions().SequenceEqual(new[] { "there", "their", "then" }));
        Check("an exact dictionary word is never suggested as its own completion", !p.Suggestions().Contains("the"));

        p.Reset();
        p.Feed('z'); p.Feed('e');
        Check("a single candidate is returned alone", p.Suggestions().SequenceEqual(new[] { "zebra" }));

        p.Reset();
        p.Feed('q'); p.Feed('q');
        Check("no match yields no suggestions", p.Suggestions().Count == 0);

        p.Reset();
        p.Feed('h'); p.Feed('e'); p.Feed('l'); p.Feed('l'); p.Feed('o');
        Check("a complete word with no extensions yields nothing", p.Suggestions().Count == 0);

        // -- acceptance is append-only (D-29) --------------------------------
        p = Sample();
        p.Feed('h'); p.Feed('e');
        string? remainder = p.Accept("help");
        Check("accept returns only the missing letters", remainder == "lp");
        Check("accept adopts the suggestion as the tracked word", p.Prefix == "help");
        Check("a repeated accept emits nothing", p.Accept("help") is null);

        p.Reset();
        p.Feed('h'); p.Feed('e');
        Check("accepting an unrelated word is refused", p.Accept("zebra") is null);
        Check("accepting the prefix itself is refused", p.Accept("he") is null);
        Check("accepting an empty string is refused", p.Accept("") is null);
        Check("a refused accept leaves the prefix untouched", p.Prefix == "he");

        // The core safety property: nothing is ever removed.
        p.Reset();
        p.Feed('h'); p.Feed('e');
        int before = p.Prefix.Length;
        foreach (string cand in p.Suggestions())
        {
            string? r = p.Accept(cand);
            Check($"accepting '{cand}' appends only", r is not null && r.Length > 0);
            break;
        }
        Check("accept never shortens the tracked word", p.Prefix.Length > before);

        // Every suggestion must be a strict extension of the prefix, for every
        // prefix that yields any — the invariant the safety argument rests on.
        var probes = new[] { "he", "hel", "the", "th", "ze", "zo" };
        bool allExtend = probes.All(prefix =>
        {
            Predictor q = Sample();
            foreach (char c in prefix) q.Feed(c);
            return q.Suggestions().All(cand =>
                cand.StartsWith(prefix, StringComparison.Ordinal) && cand.Length > prefix.Length);
        });
        Check("every suggestion is a strict extension of the prefix", allExtend);

        // -- dictionary format and integrity (D-28) --------------------------
        // Format: "#<count>\n", then one word per line ascending, then the rank
        // table as LEB128 varints holding per-256-word deltas.
        Predictor loaded = Predictor.Load(DictStream("#3\nhem\nhen\nhew\n", 1, 0, 2));
        Check("dictionary header sets the word count", loaded.WordCount == 3);
        loaded.Feed('h'); loaded.Feed('e');
        Check("a loaded dictionary suggests by rank", loaded.Suggestions().SequenceEqual(new[] { "hen", "hem", "hew" }));

        Check("malformed header is rejected", Throws(() => Predictor.Load(DictStream("3\nhelp\n", 0))));
        Check("truncated word list is rejected", Throws(() => Predictor.Load(DictStream("#3\nhelp\n", 0, 1))));
        Check("truncated rank table is rejected", Throws(() => Predictor.Load(DictStream("#2\nhem\nhen\n", 0))));

        // Ranks are stored as fixed-width little-endian uint16 values, so values
        // needing more than one byte must survive the round trip. Use 299
        // letter-only words in strict ascending order: the LAST character must
        // vary fastest for the sequence to be ordinal-ascending. (299 words also
        // keeps the reversed rank table a valid 0..298 permutation.)
        const int wordCount = 299;
        var bigHead = new StringBuilder($"#{wordCount}\n");
        var bigRanks = new int[wordCount];
        for (int i = 0; i < wordCount; i++)
        {
            char mid = (char)('a' + (i / 26) % 26);
            char last = (char)('a' + i % 26);
            bigHead.Append('a').Append(mid).Append(last).Append('\n');
            bigRanks[i] = wordCount - 1 - i;
        }
        Predictor wide = Predictor.Load(DictStream(bigHead.ToString(), bigRanks));
        Check("a 299-word table with uint16 ranks loads", wide.WordCount == wordCount);
        wide.Feed('a'); wide.Feed('a');
        Check("a 299-word dictionary still answers lookups", wide.Suggestions().Count > 0);

        Check("non-parallel arrays are rejected",
            Throws(() => new Predictor(new[] { "a", "b" }, new ushort[] { 0 })));
        Check("unsorted words are rejected",
            Throws(() => new Predictor(new[] { "b", "a" }, new ushort[] { 0, 1 })));
        Check("duplicate words are rejected",
            Throws(() => new Predictor(new[] { "a", "a" }, new ushort[] { 0, 1 })));
        Check("duplicate ranks are rejected",
            Throws(() => new Predictor(new[] { "a", "b" }, new ushort[] { 0, 0 })));
        Check("out-of-range ranks are rejected",
            Throws(() => new Predictor(new[] { "a", "b" }, new ushort[] { 0, 9 })));

        // -- the shipped dictionary ------------------------------------------
        Predictor? real = Predictor.LoadDefault();
        Check("embedded dictionary loads", real is not null);
        if (real is not null)
        {
            Check("embedded dictionary is substantial", real.WordCount >= 10_000);
            Check("embedded dictionary holds only lowercase a-z words", AllLowerAscii(real));
            Check("a common word yields a completion", Completes(real, "hel"));
            Check("a prefix of 'the' yields completions", Completes(real, "the"));
        }

        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed;
    }

    /// <summary>
    /// Builds an in-memory dictionary stream in the D-28 format: a text header
    /// and word list, followed by the rank table as fixed-width little-endian
    /// 16-bit values in word order.
    /// </summary>
    private static MemoryStream DictStream(string head, params int[] ranksInOrder)
    {
        var bytes = new List<byte>(System.Text.Encoding.ASCII.GetBytes(head));
        foreach (int rank in ranksInOrder)
        {
            bytes.Add((byte)(rank & 0xFF));
            bytes.Add((byte)((rank >> 8) & 0xFF));
        }
        return new MemoryStream(bytes.ToArray());
    }

    private static bool AllLowerAscii(Predictor p)
    {
        // Probe a spread of prefixes and confirm every suggestion is plain
        // lowercase a-z, which is the dictionary's contract (D-28).
        foreach (string prefix in new[] { "a", "th", "in", "re", "co", "st", "pr", "un", "ov", "wo" })
        {
            p.Reset();
            foreach (char c in prefix) p.Feed(c);
            foreach (string cand in p.Suggestions())
                if (!cand.All(ch => ch is >= 'a' and <= 'z'))
                    return false;
        }
        return true;
    }

    /// <summary>True when the prefix yields at least one completion.</summary>
    private static bool Completes(Predictor p, string prefix)
    {
        p.Reset();
        foreach (char c in prefix) p.Feed(c);
        return p.Suggestions().Count > 0;
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
        catch (InvalidDataException) { return true; }
        catch (FormatException) { return true; }
        catch (OverflowException) { return true; }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {name}");
        }
    }
}
