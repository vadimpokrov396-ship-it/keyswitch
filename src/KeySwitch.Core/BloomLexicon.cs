using System.Reflection;
using System.Text;

namespace KeySwitch.Core;

internal sealed class BloomLexicon
{
    private readonly byte[] bits;
    private readonly uint bitCount;
    private readonly int probes;

    public BloomLexicon(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing lexicon {resource}");
        using var input = new BinaryReader(stream);
        if (new string(input.ReadChars(4)) != "KSB1") throw new InvalidDataException("Invalid lexicon");
        bitCount = input.ReadUInt32();
        probes = input.ReadInt32();
        bits = input.ReadBytes(checked((int)(bitCount / 8)));
        if (bits.Length != bitCount / 8 || probes != 7) throw new InvalidDataException("Truncated lexicon");
    }

    public bool Contains(string word)
    {
        ulong a = 14695981039346656037UL;
        ulong b = 1099511628211UL;
        // OpenCorpora preserves ё, while ordinary Russian text often spells it е.
        foreach (byte c in Encoding.UTF8.GetBytes(word.ToLowerInvariant().Replace('ё', 'е')))
        {
            a = unchecked((a ^ c) * 1099511628211UL);
            b = unchecked((b ^ c) * 14029467366897019727UL);
        }
        b |= 1;
        for (int i = 0; i < probes; i++)
        {
            uint index = (uint)((a + unchecked((ulong)i * b)) % bitCount);
            if ((bits[(int)(index >> 3)] & (1 << (int)(index & 7))) == 0) return false;
        }
        return true;
    }
}
