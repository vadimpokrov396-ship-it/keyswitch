using System.Text;
namespace KeySwitch.Core;

public static class LayoutMap
{
    private const string English = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    private const string Russian = "ёйцукенгшщзхъфывапролджэячсмитьбю";
    private static readonly Dictionary<char, char> Map = BuildMap();
    private static Dictionary<char, char> BuildMap()
    {
        var map = new Dictionary<char, char>();
        for (int i = 0; i < English.Length; i++)
        {
            map[English[i]] = Russian[i];
            map[Russian[i]] = English[i];
            if (char.IsLetter(English[i])) map[char.ToUpperInvariant(English[i])] = char.ToUpperInvariant(Russian[i]);
            map[char.ToUpperInvariant(Russian[i])] = char.IsLetter(English[i]) ? char.ToUpperInvariant(English[i]) : English[i];
        }
        const string shiftedEn = "~{}:\"<>";
        const string shiftedRu = "ЁХЪЖЭБЮ";
        for (int i = 0; i < shiftedEn.Length; i++) { map[shiftedEn[i]] = shiftedRu[i]; map[shiftedRu[i]] = shiftedEn[i]; }
        return map;
    }
    public static string Convert(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (char c in text) result.Append(Map.GetValueOrDefault(c, c));
        return result.ToString();
    }
}
