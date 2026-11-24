using System.Globalization;
using System.Text;
using Net.Codecrete.QrCodeGenerator;

namespace Coglatas.Web.Models;

public static class AqQrDocuments
{
    public static byte[] Png(string surveyUrl) =>
        Encode(surveyUrl).ToPngBitmap(4, 8, QrCode.RgbColor(0, 0, 0), QrCode.RgbColor(255, 255, 255));

    public static byte[] Svg(string surveyUrl)
    {
        var svg = Encode(surveyUrl).ToSvgString(4, foreground: "#000000", background: "#ffffff");
        // Ship the self-contained element without the renderer's external DTD.
        return Encoding.UTF8.GetBytes(svg[svg.IndexOf("<svg ", StringComparison.Ordinal)..]);
    }

    public static byte[] Pdf(IReadOnlyList<(string Title, string SurveyUrl)> entries)
    {
        using var document = new MemoryStream();
        var offsets = new List<long> { 0 };
        void Write(string text) => document.Write(Encoding.ASCII.GetBytes(text));
        void Object(string body)
        {
            offsets.Add(document.Position);
            Write(FormattableString.Invariant($"{offsets.Count - 1} 0 obj\n{body}\nendobj\n"));
        }

        const int cardsPerPage = 4;
        var pageCount = (entries.Count + cardsPerPage - 1) / cardsPerPage;
        Write("%PDF-1.4\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        var pages = string.Join(' ', Enumerable.Range(0, pageCount).Select(page => $"{5 + page * 2} 0 R"));
        Object($"<< /Type /Pages /Count {pageCount} /Kids [{pages}] >>");
        // PDF readers supply the standard Japanese CJK font mapping. Titles are
        // hex-encoded UTF-16, so user text cannot introduce PDF drawing operators.
        Object("<< /Type /Font /Subtype /Type0 /BaseFont /HeiseiKakuGo-W5 /Encoding /UniJIS-UTF16-H /DescendantFonts [4 0 R] >>");
        Object("<< /Type /Font /Subtype /CIDFontType0 /BaseFont /HeiseiKakuGo-W5 /CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 5 >> /DW 1000 >>");
        for (var page = 0; page < pageCount; page++)
        {
            var contents = new StringBuilder();
            foreach (var (entry, index) in entries.Skip(page * cardsPerPage).Take(cardsPerPage).Select((entry, index) => (entry, index)))
                AppendCard(contents, entry.Title, entry.SurveyUrl, index);

            var contentObject = 6 + page * 2;
            Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObject} 0 R >>");
            Object($"<< /Length {contents.Length} >>\nstream\n{contents}endstream");
        }

        var crossReference = document.Position;
        Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n");
        return document.ToArray();
    }

    private static void AppendCard(StringBuilder drawing, string title, string url, int index)
    {
        var x = index % 2 == 0 ? 40 : 315;
        var y = index < 2 ? 490 : 105;
        var qr = Encode(url);
        var step = 190 / (qr.Size + 8);
        var left = x + 4 * step;
        var bottom = y + 4 * step;
        drawing.AppendLine("0 g");
        for (var row = 0; row < qr.Size; row++)
        {
            for (var column = 0; column < qr.Size; column++)
            {
                if (qr.GetModule(column, row))
                    drawing.Append(FormattableString.Invariant($"{left + column * step} {bottom + (qr.Size - 1 - row) * step} {step} {step} re\n"));
            }
        }
        drawing.AppendLine("f");
        var lineNumber = 0;
        foreach (var line in title.EnumerateRunes().Chunk(22))
        {
            var text = string.Concat(line.Select(rune => Rune.IsControl(rune) ? " " : rune.ToString()));
            var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text));
            drawing.Append(FormattableString.Invariant($"BT /F1 10 Tf 1 0 0 1 {x} {y + 212 + (9 - lineNumber++) * 11} Tm <{hex}> Tj ET\n"));
        }
    }

    private static QrCode Encode(string surveyUrl) => QrCode.EncodeText(surveyUrl, QrCode.Ecc.Medium);
}
