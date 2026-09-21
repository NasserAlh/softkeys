using System.IO;
using System.IO.Compression;
using System.Text;

namespace Softkeys;

/// <summary>
/// A4/CR-21: English word completion. Holds the prefix index over an embedded
/// dictionary and a shadow buffer of the text softkeys believes it typed.
///
/// NF-01 shapes the whole design: softkeys can never read the target text
/// field, so the buffer is rebuilt only from characters softkeys emitted
/// itself. It can therefore go stale (physical typing, mouse clicks, the app
/// changing its own text). That is why this feature is completion-only —
/// accepting a suggestion only ever appends letters, so a stale buffer can
/// never cause a deletion.
/// </summary>
public sealed class Predictor
{
    /// <summary>Suggestions appear only from this prefix length (A4 §D-24).</summary>
    public const int MinPrefixLength = 2;

    /// <summary>Candidate slots on the strip (A4 §D-24).</summary>
    public const int MaxSuggestions = 3;

    private readonly string[] _words;   // ordinal-ascending (A4 §D-25)
    private readonly ushort[] _ranks;   // parallel: 0 = most frequent
    private readonly StringBuilder _buffer = new();

    public Predictor(string[] words, ushort[] ranks)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(ranks);
        if (words.Length != ranks.Length)
            throw new ArgumentException("words and ranks must be parallel arrays", nameof(ranks));

        // Integrity: the loader and the tests both rely on these. A dictionary
        // that is not sorted cannot be binary-searched; a rank table that is not
        // a permutation would distort suggestion ordering.
        for (int i = 1; i < words.Length; i++)
            if (string.CompareOrdinal(words[i - 1], words[i]) >= 0)
                throw new ArgumentException($"dictionary not strictly ascending at index {i}: '{words[i - 1]}' >= '{words[i]}'", nameof(words));

        var seen = new bool[ranks.Length];
        foreach (ushort r in ranks)
        {
            if (r >= ranks.Length)
                throw new ArgumentException($"rank {r} out of range for {ranks.Length} words", nameof(ranks));
            if (seen[r])
                throw new ArgumentException($"rank {r} appears more than once", nameof(ranks));
            seen[r] = true;
        }

        _words = words;
        _ranks = ranks;
    }

    /// <summary>The tracked word fragment, or empty when there is none.</summary>
    public string Prefix => _buffer.ToString();

    public int WordCount => _words.Length;

    /// <summary>Drops the shadow buffer (no text is emitted).</summary>
    public void Reset() => _buffer.Clear();

    /// <summary>
    /// Feeds one emitted character into the shadow buffer. Called after every
    /// emit — including auto-repeat ticks and accepted suggestions — so the
    /// buffer always mirrors softkeys' own output.
    /// </summary>
    public void Feed(char emittedChar)
    {
        if (char.IsAsciiLetter(emittedChar))
        {
            _buffer.Append(char.ToLowerInvariant(emittedChar));
            return;
        }

        // Everything else ends or invalidates the word. Note this covers the
        // Arabic case for free: letters arrive as non-ASCII codepoints, which
        // reset the buffer and leave the strip empty. No layout detection is
        // possible under NF-01, and none is needed (A4 §D-26).
        Reset();
    }

    /// <summary>
    /// The eight sticky latches (F-05). Arming or releasing one does not change
    /// which word is being typed, so these must not end the tracked word
    /// (A4 step 3: "Shift, Ctrl, Alt, Super are not boundaries").
    /// </summary>
    private static readonly HashSet<string> ModifierKeys = new(StringComparer.Ordinal)
    {
        "Shift_L", "Shift_R", "Ctrl_L", "Ctrl_R", "Alt_L", "Alt_R", "Super_L", "Super_R",
    };

    /// <summary>
    /// Feeds an emitted key by name. Keys that move the caret or change modes
    /// (Backspace, Delete, Tab, Enter, Esc, arrows) end the tracked word;
    /// sticky modifiers do not; letters are treated as text.
    ///
    /// Backspace and Delete are treated as word-enders rather than as "pop one
    /// character": the caret position is unknown, so guessing would risk
    /// appending a completion into the middle of a word. Dropping the fragment
    /// costs suggestions until the next word and cannot corrupt anything
    /// (A4 §D-27).
    /// </summary>
    public void FeedKey(string keyName)
    {
        if (ModifierKeys.Contains(keyName))
            return;

        if (keyName.Length == 1 && char.IsAsciiLetter(keyName[0]))
        {
            // A bare letter keypress without Shift/CapsLock, used by callers that
            // do not compute an explicit character.
            Feed(keyName[0]);
            return;
        }

        Reset();
    }

    /// <summary>
    /// Up to <see cref="MaxSuggestions"/> completions for the tracked prefix,
    /// most frequent first. Empty when the prefix is too short or nothing
    /// matches. Suggestions always extend the prefix and always differ from it,
    /// so accepting one can never emit zero characters or delete anything.
    /// </summary>
    public IReadOnlyList<string> Suggestions()
    {
        if (_buffer.Length < MinPrefixLength)
            return Array.Empty<string>();

        string prefix = _buffer.ToString();
        int lo = LowerBound(prefix);

        // Walk the prefix range (which is contiguous in ordinal order) and keep
        // the MaxSuggestions best-ranked words seen (A4 §D-25). Fixed-size scan,
        // no allocation beyond the result.
        Span<int> bestIdx = stackalloc int[MaxSuggestions];
        Span<int> bestRank = stackalloc int[MaxSuggestions];
        bestRank.Fill(int.MaxValue);
        int found = 0;

        for (int i = lo; i < _words.Length; i++)
        {
            if (!_words[i].StartsWith(prefix, StringComparison.Ordinal))
                break;
            if (_words[i].Length == prefix.Length)
                continue; // completion only: never suggest the prefix itself

            int rank = _ranks[i];
            if (found == MaxSuggestions && rank >= bestRank[MaxSuggestions - 1])
                continue;

            int pos = found < MaxSuggestions ? found++ : MaxSuggestions - 1;
            while (pos > 0 && bestRank[pos - 1] > rank)
            {
                bestRank[pos] = bestRank[pos - 1];
                bestIdx[pos] = bestIdx[pos - 1];
                pos--;
            }
            bestRank[pos] = rank;
            bestIdx[pos] = i;
        }

        if (found == 0)
            return Array.Empty<string>();

        var result = new string[found];
        for (int i = 0; i < found; i++)
            result[i] = _words[bestIdx[i]];
        return result;
    }

    /// <summary>
    /// Accepts a suggestion, returning exactly the text still to be emitted so
    /// that prefix + returned = suggestion (append-only, never negative).
    /// Updates the tracked prefix on success so a repeated accept is a no-op.
    /// Returns null when the suggestion is not a valid extension of the prefix.
    /// </summary>
    public string? Accept(string suggestion)
    {
        if (string.IsNullOrEmpty(suggestion))
            return null;

        string prefix = _buffer.ToString();
        if (!suggestion.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        if (suggestion.Length <= prefix.Length)
            return null;

        string remainder = suggestion[prefix.Length..];
        _buffer.Append(remainder);
        return remainder;
    }

    /// <summary>First index whose word is >= <paramref name="prefix"/> (ordinal).</summary>
    private int LowerBound(string prefix)
    {
        int lo = 0, hi = _words.Length;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (string.CompareOrdinal(_words[mid], prefix) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    // -- Dictionary loading --------------------------------------------------

    /// <summary>Logical resource name set by softkeys.csproj (D-28).</summary>
    public const string ResourceName = "softkeys.words";

    /// <summary>
    /// Loads the embedded dictionary, or returns null when the resource is
    /// absent or unreadable. Prediction is a convenience: a missing dictionary
    /// must never stop the keyboard from starting (F-10 spirit).
    /// </summary>
    public static Predictor? LoadDefault()
    {
        try
        {
            using Stream? stream = typeof(Predictor).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return null;
            using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
            return Load(brotli);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the dictionary format (D-28): a `<c>#count</c>` header, then that
    /// many words in ascending ordinal order one per line, then the rank table
    /// as fixed-width little-endian 16-bit values in word order.
    /// </summary>
    internal static Predictor Load(Stream stream)
    {
        string header = ReadLine(stream) ?? throw new InvalidDataException("dictionary header missing");
        if (header.Length == 0 || header[0] != '#')
            throw new InvalidDataException($"dictionary header malformed: '{header}'");
        int count = int.Parse(header.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture);
        if (count <= 0)
            throw new InvalidDataException($"bad dictionary count {count}");

        var words = new string[count];
        for (int i = 0; i < count; i++)
            words[i] = ReadLine(stream) ?? throw new InvalidDataException($"dictionary truncated at word {i}/{count}");

        var ranks = new ushort[count];
        var raw = new byte[count * 2];
        int read = 0;
        while (read < raw.Length)
        {
            int n = stream.Read(raw, read, raw.Length - read);
            if (n <= 0)
                throw new InvalidDataException($"rank table truncated at byte {read}/{raw.Length}");
            read += n;
        }
        for (int i = 0; i < count; i++)
            ranks[i] = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8));

        return new Predictor(words, ranks);
    }

    /// <summary>Reads one LF-terminated ASCII line, one byte at a time.</summary>
    private static string? ReadLine(Stream stream)
    {
        var bytes = new List<byte>(24);
        int b;
        while ((b = stream.ReadByte()) >= 0)
        {
            if (b == '\n')
                return Encoding.ASCII.GetString(bytes.ToArray());
            if (b != '\r')
                bytes.Add((byte)b);
        }
        return bytes.Count > 0 ? Encoding.ASCII.GetString(bytes.ToArray()) : null;
    }
}
