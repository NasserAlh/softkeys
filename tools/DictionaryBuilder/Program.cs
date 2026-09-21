using System.Globalization;
using System.IO.Compression;
using System.Text;

// A4/D-28: builds the embedded English completion dictionary.
//
// Corpus mode - derive the vocabulary and ranks from scratch:
//   dotnet run --project tools/DictionaryBuilder -- <corpusDir> <vocabularyFile> [outFile] [maxWords]
//
// Pack mode - emit the blob from an already-curated alphabetical word list plus
// its parallel rank file (ranks are 0-based, rank 0 = most frequent):
//   dotnet run --project tools/DictionaryBuilder -- --pack <wordsFile> <ranksFile> [outFile]
//
// Both modes are reproducible offline from committed inputs. Everything
// consumed is public domain: Project Gutenberg prose for the frequency counts,
// and a word list whose licence permits redistribution (the dwyl/english-words
// list is Unlicense). No corpus-derived or research-licensed frequency data is
// used anywhere - see the A4 assessment, section 7.
//
// Output format (one Brotli stream):
//   #<count>\n
//   <word>\t<rank>\n   ... <count> lines, strictly ascending ordinal word order
// The predictor re-validates all of this on load, so a malformed blob fails
// loudly in the selftest rather than silently producing bad suggestions.

if (args.Length >= 4 && args[0] == "--pack")
    return Pack(args[1], args[2], args[3]);

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dictbuild <corpusDir> <vocabularyFile> [outFile] [maxWords]");
    Console.Error.WriteLine("       dictbuild --pack <wordsFile> <ranksFile> [outFile]");
    return 2;
}

string corpusDir = args[0];
string vocabularyFile = args[1];
string outFile = args.Length > 2 ? args[2] : Path.Combine("assets", "softkeys-words.br");
int maxWords = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 30000;
const int minCount = 2;

if (!Directory.Exists(corpusDir))
{
    Console.Error.WriteLine($"corpus directory not found: {corpusDir}");
    return 1;
}
if (!File.Exists(vocabularyFile))
{
    Console.Error.WriteLine($"vocabulary file not found: {vocabularyFile}");
    return 1;
}

// -- 1. vocabulary set -------------------------------------------------------
var vocabulary = new HashSet<string>(StringComparer.Ordinal);
foreach (string raw in File.ReadLines(vocabularyFile))
{
    string word = raw.Trim().ToLowerInvariant();
    if (word.Length is < 2 or > 20) continue;
    if (!word.All(c => c is >= 'a' and <= 'z')) continue;
    vocabulary.Add(word);
}
Console.WriteLine($"vocabulary        : {vocabulary.Count:N0} words");

// -- 2. count over the corpus ------------------------------------------------
// Project Gutenberg wraps each book in a licence header/footer. Those blocks are
// not the author's prose but they are dense in words like "project",
// "copyright", "foundation", "electronic" and "terms" - enough to push several of
// them into the top 500 ranks where they would surface as suggestions. Strip the
// wrapper before counting. Measured effect of not stripping: PG boilerplate is
// only 2.5% of tokens but moves 57% of the shared top-30k words by more than 100
// ranks.
var counts = new Dictionary<string, long>(StringComparer.Ordinal);
long tokens = 0, chars = 0, stripped = 0;
foreach (string file in Directory.GetFiles(corpusDir, "*.txt"))
{
    string text = File.ReadAllText(file);
    chars += text.Length;
    string body = StripGutenbergWrapper(text);
    stripped += text.Length - body.Length;
    var sb = new StringBuilder();
    foreach (char c in body)
    {
        if (char.IsAsciiLetter(c))
        {
            sb.Append(char.ToLowerInvariant(c));
            continue;
        }

        if (sb.Length > 0)
        {
            tokens++;
            if (sb.Length is >= 2 and <= 20)
            {
                string token = sb.ToString();
                if (vocabulary.Contains(token))
                    counts[token] = counts.GetValueOrDefault(token) + 1;
            }
            sb.Clear();
        }
    }
}
Console.WriteLine($"corpus            : {chars:N0} chars, {tokens:N0} tokens, {Directory.GetFiles(corpusDir, "*.txt").Length} files");
Console.WriteLine($"PG wrapper removed: {stripped:N0} chars ({100.0 * stripped / Math.Max(1, chars):F1}%)");
Console.WriteLine($"vocabulary seen   : {counts.Count:N0} words (count >= 1)");

// -- 3. rank by frequency ----------------------------------------------------
var ranked = counts.Where(kv => kv.Value >= minCount)
                   .OrderByDescending(kv => kv.Value)
                   .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                   .Select(kv => kv.Key)
                   .Take(maxWords)
                   .ToArray();
if (ranked.Length == 0)
{
    Console.Error.WriteLine($"no words reached the count >= {minCount} threshold");
    return 1;
}
Console.WriteLine($"dictionary        : {ranked.Length:N0} words (count >= {minCount}, capped at {maxWords:N0})");

// ranks[word] = frequency rank, 0 = most frequent
var ranks = new Dictionary<string, int>(ranked.Length, StringComparer.Ordinal);
for (int i = 0; i < ranked.Length; i++)
    ranks[ranked[i]] = i;

// -- 4/5. emit alphabetically with the parallel rank, then compress ----------
string[] alphabetical = ranked.OrderBy(w => w, StringComparer.Ordinal).ToArray();
long rawLength = WriteBlob(alphabetical, i => ranks[alphabetical[i]], outFile);

Console.WriteLine();
Console.WriteLine($"raw payload       : {rawLength:N0} bytes ({rawLength / 1024.0:F1} KB)");
long brotliSize = new FileInfo(outFile).Length;
Console.WriteLine($"brotli blob       : {brotliSize:N0} bytes ({brotliSize / 1024.0:F1} KB)  ratio {100.0 * brotliSize / rawLength:F1}%");
Console.WriteLine($"written           : {Path.GetFullPath(outFile)}");
Console.WriteLine();
Console.WriteLine("top 20 by frequency:");
for (int i = 0; i < Math.Min(20, ranked.Length); i++)
    Console.WriteLine($"  {i,3}  {ranked[i],-16} {counts[ranked[i]],12:N0}");

return 0;

// -- pack mode ---------------------------------------------------------------

/// <summary>
/// Removes Project Gutenberg's licence wrapper, returning only the book proper.
/// Falls back to the whole text when the markers are absent, so a corpus that
/// does not use them is still counted rather than silently dropped.
/// </summary>
static string StripGutenbergWrapper(string text)
{
    const string startMarker = "*** START OF THE PROJECT GUTENBERG";
    const string endMarker = "*** END OF THE PROJECT GUTENBERG";

    int start = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
    if (start >= 0)
    {
        int lineEnd = text.IndexOf('\n', start);
        start = lineEnd >= 0 ? lineEnd + 1 : start;
    }
    else
    {
        start = 0;
    }

    int end = text.IndexOf(endMarker, start, StringComparison.OrdinalIgnoreCase);
    if (end < 0)
        end = text.Length;

    return start < end ? text[start..end] : text;
}

static int Pack(string wordsFile, string ranksFile, string outFile)
{
    if (!File.Exists(wordsFile)) { Console.Error.WriteLine($"not found: {wordsFile}"); return 1; }
    if (!File.Exists(ranksFile)) { Console.Error.WriteLine($"not found: {ranksFile}"); return 1; }

    string[] words = File.ReadAllLines(wordsFile).Where(l => l.Length > 0).ToArray();
    string[] rankLines = File.ReadAllLines(ranksFile).Where(l => l.Length > 0).ToArray();
    if (words.Length != rankLines.Length)
    {
        Console.Error.WriteLine($"line count mismatch: {words.Length} words vs {rankLines.Length} ranks");
        return 1;
    }

    var ranks = new int[words.Length];
    for (int i = 0; i < words.Length; i++)
        ranks[i] = int.Parse(rankLines[i], CultureInfo.InvariantCulture);

    long rawLength = WriteBlob(words, i => ranks[i], outFile);
    long brotliSize = new FileInfo(outFile).Length;

    Console.WriteLine($"words             : {words.Length:N0}");
    Console.WriteLine($"raw payload       : {rawLength:N0} bytes ({rawLength / 1024.0:F1} KB)");
    Console.WriteLine($"brotli blob       : {brotliSize:N0} bytes ({brotliSize / 1024.0:F1} KB)  ratio {100.0 * brotliSize / rawLength:F1}%");
    Console.WriteLine($"written           : {Path.GetFullPath(outFile)}");
    return 0;
}

/// <summary>
/// Writes the packed, Brotli-compressed dictionary; returns the raw byte count.
///
/// Layout (D-28): the word list as text, then the rank table as fixed-width
/// little-endian 16-bit values in word order.
///
/// Ranks are fixed-width rather than varint deltas for a deliberate reason: rank
/// order is a permutation, not a monotonic series, so deltas go negative all the
/// time and a naive unsigned varint then expands -1 into five bytes - which is
/// both larger than the fixed form and a genuine correctness trap (an earlier
/// revision did exactly that and produced garbage ranks). A uint16 holds any
/// dictionary below 65,536 words, which this is by a wide margin.
/// </summary>
static long WriteBlob(string[] alphabetical, Func<int, int> rankAt, string outFile)
{
    var head = new StringBuilder(alphabetical.Length * 12);
    head.Append('#').Append(alphabetical.Length).Append('\n');
    foreach (string word in alphabetical)
        head.Append(word).Append('\n');

    byte[] words = Encoding.UTF8.GetBytes(head.ToString());

    if (alphabetical.Length > ushort.MaxValue)
        throw new InvalidOperationException($"dictionary of {alphabetical.Length} words exceeds the uint16 rank table");

    byte[] packedRanks = new byte[alphabetical.Length * 2];
    for (int i = 0; i < alphabetical.Length; i++)
    {
        int rank = rankAt(i);
        if (rank is < 0 or > ushort.MaxValue)
            throw new InvalidOperationException($"rank {rank} out of range at index {i}");
        packedRanks[i * 2] = (byte)(rank & 0xFF);
        packedRanks[i * 2 + 1] = (byte)(rank >> 8);
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
    using (var file = File.Create(outFile))
    using (var brotli = new BrotliStream(file, CompressionLevel.SmallestSize))
    {
        brotli.Write(words, 0, words.Length);
        brotli.Write(packedRanks, 0, packedRanks.Length);
    }

    return words.Length + packedRanks.Length;
}
