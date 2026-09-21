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
var counts = new Dictionary<string, long>(StringComparer.Ordinal);
long tokens = 0, chars = 0;
foreach (string file in Directory.GetFiles(corpusDir, "*.txt"))
{
    string text = File.ReadAllText(file);
    chars += text.Length;
    var sb = new StringBuilder();
    foreach (char c in text)
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

/// <summary>Writes the packed, Brotli-compressed dictionary; returns the raw byte count.</summary>
static long WriteBlob(string[] alphabetical, Func<int, int> rankAt, string outFile)
{
    var payload = new StringBuilder(alphabetical.Length * 12);
    payload.Append('#').Append(alphabetical.Length).Append('\n');
    for (int i = 0; i < alphabetical.Length; i++)
        payload.Append(alphabetical[i]).Append('\t').Append(rankAt(i).ToString(CultureInfo.InvariantCulture)).Append('\n');

    byte[] raw = Encoding.UTF8.GetBytes(payload.ToString());
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!);
    using (var file = File.Create(outFile))
    using (var brotli = new BrotliStream(file, CompressionLevel.SmallestSize))
        brotli.Write(raw, 0, raw.Length);
    return raw.Length;
}
