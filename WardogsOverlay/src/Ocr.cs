using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace WardogsOverlay;

sealed record OcrWord(string Text, RectI Box);
sealed record OcrLineText(string Text, List<OcrWord> Words);

/// <summary>
/// Встроенное в Windows распознавание текста (Windows.Media.Ocr): без нейросетей в комплекте,
/// без Python, работает на CPU за десятки миллисекунд.
/// </summary>
sealed class OcrService
{
    readonly OcrEngine ru;
    readonly OcrEngine en;

    public OcrService()
    {
        // Русский движок читает и кириллицу, и латиницу. Если его нет (английская Windows) —
        // работаем на английском: так распознаётся английская версия игры.
        var ruEngine = OcrEngine.TryCreateFromLanguage(new Language("ru"));
        var enEngine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                       ?? OcrEngine.TryCreateFromUserProfileLanguages();
        ru = ruEngine ?? enEngine ?? throw new InvalidOperationException(L.T(
            "В Windows нет ни одного языка распознавания текста. Параметры → Время и язык → Язык → добавить «Русский».",
            "Windows has no text recognition language installed. Settings → Time & language → Language → add English or Russian."));
        en = enEngine ?? ru;
        HasRussian = ruEngine != null;
    }

    public bool HasRussian { get; }

    /// <summary>Основной движок: русский, если есть (читает и латиницу), иначе английский.</summary>
    public List<OcrLineText> Russian(Frame f) => Recognize(ru, f);
    public List<OcrLineText> English(Frame f) => Recognize(en, f);

    static List<OcrLineText> Recognize(OcrEngine engine, Frame f)
    {
        int max = (int)OcrEngine.MaxImageDimension;
        if (f.W <= max && f.H <= max) return RecognizeOne(engine, f, 0, 0);

        // Ультраширокие кадры режем на перекрывающиеся полосы.
        var all = new List<OcrLineText>();
        int step = max - 300;
        for (int x = 0; x < f.W; x += step)
        {
            int w = Math.Min(max, f.W - x);
            var part = f.Crop(new RectI(x, 0, w, Math.Min(f.H, max)));
            foreach (var line in RecognizeOne(engine, part, x, 0))
            {
                bool dup = all.Any(l => l.Words.Count > 0 && line.Words.Count > 0 &&
                                        Math.Abs(l.Words[0].Box.X - line.Words[0].Box.X) < 4 &&
                                        Math.Abs(l.Words[0].Box.Y - line.Words[0].Box.Y) < 4);
                if (!dup) all.Add(line);
            }
            if (x + w >= f.W) break;
        }
        return all;
    }

    static List<OcrLineText> RecognizeOne(OcrEngine engine, Frame f, int ox, int oy)
    {
        // Строки серой картинки Windows выравнивает по 4 байта: при ширине, не кратной 4, изображение
        // «съезжает» и OCR не видит ничего. Поэтому дополняем справа до кратной 4 ширины краевым цветом.
        if (f.W % 4 != 0) f = PadWidth(f);
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(f.Gray().AsBuffer(), BitmapPixelFormat.Gray8, f.W, f.H);
        var result = engine.RecognizeAsync(bmp).AsTask().GetAwaiter().GetResult();
        return ToLines(result, ox, oy);
    }

    static Frame PadWidth(Frame f)
    {
        int w = (f.W + 3) / 4 * 4;
        var dst = new Frame(w, f.H);
        for (int y = 0; y < f.H; y++)
        {
            Buffer.BlockCopy(f.Px, y * f.W * 4, dst.Px, y * w * 4, f.W * 4);
            for (int x = f.W; x < w; x++)
                Buffer.BlockCopy(f.Px, (y * f.W + f.W - 1) * 4, dst.Px, (y * w + x) * 4, 4);
        }
        return dst;
    }

    static List<OcrLineText> ToLines(OcrResult result, int ox, int oy)
    {
        var lines = new List<OcrLineText>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            var words = new List<OcrWord>(line.Words.Count);
            foreach (var w in line.Words)
            {
                var r = w.BoundingRect;
                words.Add(new OcrWord(w.Text, new RectI((int)r.X + ox, (int)r.Y + oy, (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height))));
            }
            lines.Add(new OcrLineText(line.Text, words));
        }
        return lines;
    }
}
