using System.Text;

namespace WardogsOverlay;

static class TextUtil
{
    // OCR путает латиницу и кириллицу одинакового начертания — приводим к одному алфавиту.
    const string CyrLook = "АВСЕНКМОРТХУ";
    const string LatLook = "ABCEHKMOPTXY";

    /// <summary>Верхний регистр, кириллические двойники → латиница, только A-Z и цифры.</summary>
    public static string Latinize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch0 in s.ToUpperInvariant())
        {
            char ch = ch0;
            int i = CyrLook.IndexOf(ch);
            if (i >= 0) ch = LatLook[i];
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Верхний регистр, латинские двойники → кириллица, только буквы.</summary>
    public static string Cyrillize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch0 in s.ToUpperInvariant())
        {
            char ch = ch0 == '0' ? 'О' : ch0;
            int i = LatLook.IndexOf(ch);
            if (i >= 0) ch = CyrLook[i];
            if (char.IsLetter(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public static double Similarity(string a, string b)
    {
        int m = Math.Max(a.Length, b.Length);
        return m == 0 ? 0 : 1.0 - (double)Levenshtein(a, b) / m;
    }

    /// <summary>Слово OCR совпадает с русским словом интерфейса (допускается одна ошибка).</summary>
    public static bool IsUiWord(string ocr, string target)
    {
        var w = Cyrillize(ocr);
        return Math.Abs(w.Length - target.Length) <= 1 && Levenshtein(w, target) <= 1;
    }

    /// <summary>То же для английского интерфейса игры.</summary>
    public static bool IsUiWordEn(string ocr, string target)
    {
        var w = Latinize(ocr);
        return Math.Abs(w.Length - target.Length) <= 1 && Levenshtein(w, target) <= 1;
    }

    // Как называются элементы меню в русской и английской версиях игры. Английские — предположение
    // по смыслу (скриншота английского меню пока нет), поэтому перечислены варианты.
    public static bool IsModulesWord(string ocr) =>
        IsUiWord(ocr, "МОДУЛИ") || IsUiWordEn(ocr, "MODULES") || IsUiWordEn(ocr, "ATTACHMENTS") || IsUiWordEn(ocr, "MODIFICATIONS");

    public static bool IsSlotsWord(string ocr) =>
        IsUiWord(ocr, "ЯЧЕЙКИ") || IsUiWordEn(ocr, "LOADOUT") || IsUiWordEn(ocr, "EQUIPMENT") || IsUiWordEn(ocr, "SLOTS");

    /// <summary>Верхний регистр, латинские двойники → кириллица, буквы и цифры («AK-74» → «АК74»).</summary>
    public static string NormRu(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch0 in s.ToUpperInvariant())
        {
            char ch = ch0;
            int i = LatLook.IndexOf(ch);
            if (i >= 0) ch = CyrLook[i];
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    public static int? ParsePrice(string s)
    {
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length is > 0 and <= 6 ? int.Parse(digits) : null;
    }
}
