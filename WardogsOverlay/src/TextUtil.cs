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

    /// <summary>
    /// Слово OCR совпадает с русским словом интерфейса (допускается одна ошибка). Русский текст,
    /// прочитанный английским OCR (в Windows нет русского языка распознавания), тоже узнаём —
    /// по форме букв: «МОДУЛИ» английский движок читает как «MOAYJIVI».
    /// </summary>
    public static bool IsUiWord(string ocr, string target)
    {
        var w = Cyrillize(ocr);
        if (Math.Abs(w.Length - target.Length) <= 1 && Levenshtein(w, target) <= 1) return true;
        return ShapeMatches(ocr, target);
    }

    // Как английский OCR передаёт русские буквы (по начертанию шрифта игры).
    static readonly Dictionary<char, string> CyrShape = new()
    {
        ['А'] = "A", ['Б'] = "6", ['В'] = "B", ['Г'] = "R", ['Д'] = "A", ['Е'] = "E", ['Ё'] = "E", ['Ж'] = "X",
        ['З'] = "3", ['И'] = "VI", ['Й'] = "VI", ['К'] = "K", ['Л'] = "JI", ['М'] = "M", ['Н'] = "H", ['О'] = "O",
        ['П'] = "N", ['Р'] = "P", ['С'] = "C", ['Т'] = "T", ['У'] = "Y", ['Ф'] = "O", ['Х'] = "X", ['Ц'] = "U",
        ['Ч'] = "4", ['Ш'] = "LU", ['Щ'] = "LU", ['Ъ'] = "B", ['Ы'] = "BI", ['Ь'] = "B", ['Э'] = "3", ['Ю'] = "IO", ['Я'] = "R",
    };

    /// <summary>
    /// «Скелет» строки по начертанию: русские буквы → как их видит английский OCR, похожие знаки
    /// склеены (0/O/Q, 1/I/L/|/!/J). «ПП-19 «БИЗОН»» и «nn-19 «6VI3OH»» дают одно и то же.
    /// </summary>
    public static string Shape(string s)
    {
        var sb = new StringBuilder(s.Length + 4);
        foreach (var ch0 in s.ToUpperInvariant())
        {
            if (CyrShape.TryGetValue(ch0, out var lat)) { foreach (var c in lat) Append(c); }
            else Append(ch0);
        }
        return sb.ToString();

        void Append(char c)
        {
            c = c switch
            {
                '0' or 'Q' or 'D' => 'O',
                '1' or 'L' or '|' or '!' or 'J' or 'Ӏ' => 'I',
                'U' or 'V' => 'V',
                'F' => 'P',
                _ => c,
            };
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(c);
        }
    }

    /// <summary>Похожи ли строки по начертанию (длинным словам прощается больше ошибок OCR).</summary>
    public static bool ShapeMatches(string ocr, string targetRu)
    {
        var a = Shape(ocr);
        var b = Shape(targetRu);
        if (a.Length < 3) return false;
        int allowed = b.Length <= 5 ? 0 : b.Length <= 8 ? 2 : b.Length / 3;
        return Math.Abs(a.Length - b.Length) <= allowed && Levenshtein(a, b) <= allowed;
    }

    /// <summary>То же для английского интерфейса игры.</summary>
    public static bool IsUiWordEn(string ocr, string target)
    {
        var w = Latinize(ocr);
        return Math.Abs(w.Length - target.Length) <= 1 && Levenshtein(w, target) <= 1;
    }

    // Как называются элементы меню в русской и английской версиях игры. В английской кнопка
    // модулей подписана «ATTC.», кнопка патронов — «AMMO», панель слотов — «EQUIPMENT SLOTS».
    public static bool IsModulesWord(string ocr) =>
        IsUiWord(ocr, "МОДУЛИ") || IsAttcWord(ocr) || IsUiWordEn(ocr, "MODULES") || IsUiWordEn(ocr, "ATTACHMENTS");

    /// <summary>Кнопка модулей в английской игре — короткое «ATTC.»: тут ошибок не прощаем, кроме T/I.</summary>
    static bool IsAttcWord(string ocr) => Latinize(ocr).Replace('I', 'T') is "ATTC" or "ATC" or "ATTTC";

    public static bool IsAmmoWord(string ocr) =>
        IsUiWord(ocr, "БОЕПРИПАСЫ") || Latinize(ocr).Replace('0', 'O') is "AMMO" or "AMM0" or "AMNO" or "ANMO";

    /// <summary>Подпись экрана продавца слева вверху: «// Продавец…», «EQUIPMENT VENDOR».</summary>
    public static bool IsVendorWord(string ocr) => IsUiWord(ocr, "ПРОДАВЕЦ") || IsUiWordEn(ocr, "VENDOR");

    public static bool IsSlotsWord(string ocr) =>
        IsUiWord(ocr, "ЯЧЕЙКИ") || IsUiWordEn(ocr, "EQUIPMENT") || IsUiWordEn(ocr, "SLOTS");

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
        var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 6 ? int.Parse(digits) : null;
    }

    /// <summary>
    /// Текст анонса Steam (BBCode) → обычный текст: картинки и видео выкидываем, ссылки оставляем
    /// подписью, пункты списков — с «• », заголовки — отдельной строкой.
    /// </summary>
    public static string CleanSteamText(string s)
    {
        const System.Text.RegularExpressions.RegexOptions o =
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline;
        static string R(string input, string pattern, string replacement, System.Text.RegularExpressions.RegexOptions options = default) =>
            System.Text.RegularExpressions.Regex.Replace(input, pattern, replacement, options);
        s = s.Replace("\r", "");
        s = R(s, @"\[img[^\]]*\].*?\[/img\]", "", o);
        s = R(s, @"\[(previewyoutube|video)[^\]]*\].*?\[/\1\]", "", o);
        s = R(s, @"\{STEAM_CLAN_IMAGE\}\S*", "", o);
        s = R(s, @"\[url=[^\]]*\](.*?)\[/url\]", "$1", o);
        s = R(s, @"\[\*\]", "\n• ", o);
        s = R(s, @"\[/?(h[1-6]|p|list|olist|tr|table|hr|quote)[^\]]*\]", "\n", o);
        s = R(s, @"\[/?[a-z*]+(?:[= ][^\]]*)?\]", "", o);
        s = R(s, @"<[^>]+>", "", o);
        s = System.Net.WebUtility.HtmlDecode(s);
        s = R(s, @"[ \t]+\n", "\n");
        s = R(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    /// <summary>Буквы, которые OCR путает с цифрами в суммах: О→0, З→3, б→6, В→8, l/I/|→1.</summary>
    public static string DigitLike(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch
            {
                'O' or 'o' or 'О' or 'о' or 'D' => '0',
                'З' or 'з' => '3',
                'б' or 'Б' or 'b' => '6',
                'В' or 'B' => '8',
                'l' or 'I' or '|' or '!' or 'і' => '1',
                _ => c,
            });
        return sb.ToString();
    }
}
