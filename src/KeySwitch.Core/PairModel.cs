using System.Reflection;
using System.Text;

namespace KeySwitch.Core;

/// <summary>Counts of adjacent Russian word pairs (previous word, word form) from the Leipzig corpora
/// (data/ru-pairs.bin, tools/build_ru_pairs.py). Word forms are identified by their rank in ru-typo.txt.</summary>
internal sealed class PairModel
{
    private const int RankBits = 18;
    private readonly byte[] forms;
    private readonly Dictionary<string, (int Index, double Count)> previous = new(StringComparer.Ordinal);
    private readonly uint[] keys;
    private readonly byte[] counts;
    public double TotalTokens { get; }

    private PairModel(byte[] forms, uint[] keys, byte[] counts, double total)
    {
        this.forms = forms; this.keys = keys; this.counts = counts; TotalTokens = total;
    }

    /// <summary>Loads the embedded table; null when it is not bundled or was built for another ru-typo.txt.</summary>
    public static PairModel? Load(int formCount)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("KeySwitch.ru-pairs.bin");
        using var formStream = assembly.GetManifestResourceStream("KeySwitch.ru-typo.txt");
        if (stream is null || formStream is null) return null;
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("KSP1"u8)) return null;
        int count = reader.ReadInt32();
        ulong hash = reader.ReadUInt64();
        double total = reader.ReadUInt64();
        if (count != formCount || hash != Fnv64(formStream)) return null;
        var formCounts = reader.ReadBytes(count);
        int previousCount = reader.ReadInt32();
        var words = new List<(string, double)>(previousCount);
        for (int i = 0; i < previousCount; i++)
            words.Add((Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadByte())), Decode(reader.ReadByte())));
        int pairCount = reader.ReadInt32();
        var keys = new uint[pairCount];
        uint key = 0;
        for (int i = 0; i < pairCount; i++)
        {
            uint delta = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte b = reader.ReadByte();
                delta |= (uint)(b & 0x7F) << shift;
                if (b < 0x80) break;
            }
            keys[i] = key += delta;
        }
        var model = new PairModel(formCounts, keys, reader.ReadBytes(pairCount), total);
        for (int i = 0; i < words.Count; i++) model.previous[words[i].Item1] = (i + 1, words[i].Item2);
        return model;
    }

    private static double Decode(byte q) => q == 0 ? 0 : Math.Pow(2, (q - 1) / 10.0);

    /// <summary>FNV-1a 64 of the list as built (UTF-8 lines ending in \n), whatever line endings the checkout
    /// uses (git may turn them into \r\n on Windows).</summary>
    private static ulong Fnv64(Stream stream)
    {
        ulong h = 14695981039346656037;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(line)) h = (h ^ b) * 1099511628211UL;
            h = (h ^ (byte)'\n') * 1099511628211UL;
        }
        return h;
    }

    /// <summary>Corpus count of a ranked form (0 when unranked).</summary>
    public double FormCount(int rank) => rank >= 1 && rank <= forms.Length ? Decode(forms[rank - 1]) : 0;

    /// <summary>Corpus count of the previous word as a context, or null when it is not a known context word.</summary>
    public double? PreviousCount(string? word) =>
        word is { Length: > 0 } && previous.TryGetValue(Normalize(word), out var entry) ? entry.Count : null;

    /// <summary>How often the ranked form followed the previous word (0 when never, or below the build cut-off).</summary>
    public double PairCount(string? word, int rank)
    {
        if (word is not { Length: > 0 } || rank < 1 || rank >= 1 << RankBits || !previous.TryGetValue(Normalize(word), out var entry)) return 0;
        int index = Array.BinarySearch(keys, (uint)entry.Index << RankBits | (uint)rank);
        return index >= 0 ? Decode(counts[index]) : 0;
    }

    /// <summary>Log ratio of observed to expected pair count, with pseudo-count <paramref name="alpha"/>: positive
    /// when the form is typical after the previous word, negative when it was expected but never seen there,
    /// near zero when the corpus says little. 0 for an unknown previous word.</summary>
    public double Lift(string? word, int rank, double alpha = 2)
    {
        if (PreviousCount(word) is not double previousCount || rank < 1) return 0;
        double expected = previousCount * FormCount(rank) / TotalTokens;
        return Math.Log((PairCount(word, rank) + alpha) / (expected + alpha));
    }

    private static string Normalize(string word) => word.Trim().Trim("\"'«»„“()[]{}<>".ToCharArray()).ToLowerInvariant().Replace('ё', 'е');
}
