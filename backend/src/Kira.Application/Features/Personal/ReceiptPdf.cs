using System.Globalization;
using System.Text;

namespace Kira.Application.Features.Personal;

/// <summary>
/// Builds a printable A4 PDF from a receipt scan (a JPEG). The JPEG is embedded as-is, so nothing is re-compressed.
/// A tall receipt is printed at a receipt-like width and continues across as many pages as it needs,
/// with a small overlap so no line is cut in half at a page break.
/// </summary>
public static class ReceiptPdf
{
    const double PageW = 595.28, PageH = 841.89;        // A4 in points
    const double Margin = 36, FooterH = 18, PageOverlap = 12;
    const double ReceiptWidth = 80 / 25.4 * 72 * 1.25;  // tall receipts: 80 mm paper, shown 25% larger for readability

    public static byte[] FromJpeg(byte[] jpeg, string footer)
    {
        var (w, h, components) = JpegInfo(jpeg);
        var contentW = PageW - 2 * Margin;
        var contentH = PageH - 2 * Margin - FooterH;

        // Tall, narrow scans are receipts: show them at receipt width. Wider ones (invoices) fill the page width.
        var aspect = (double)h / w;
        var displayW = aspect > 1.6 ? Math.Min(contentW, ReceiptWidth) : contentW;
        var displayH = displayW * aspect;
        if (aspect <= 1.6 && displayH > contentH) { displayH = contentH; displayW = displayH / aspect; } // fits on one page
        var step = contentH - PageOverlap;
        var pages = displayH <= contentH ? 1 : (int)Math.Ceiling((displayH - PageOverlap) / step);
        var x0 = (PageW - displayW) / 2;
        var top = PageH - Margin;

        var objects = new List<byte[]>();
        // 1 catalog, 2 pages, 3 font, 4 image, then per page: page object + content stream
        var kids = string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{5 + i * 2} 0 R"));
        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pages} >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        var colorSpace = components switch { 1 => "/DeviceGray", 4 => "/DeviceCMYK /Decode [1 0 1 0 1 0 1 0]", _ => "/DeviceRGB" };
        objects.Add(Concat(
            Ascii($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n"),
            jpeg, Ascii("\nendstream")));

        for (var i = 0; i < pages; i++)
        {
            // Clip to the content area, then place the image so this page shows its i-th slice.
            var clipY = Margin + FooterH;
            var imageBottom = top + i * step - displayH;
            if (pages == 1) imageBottom = top - displayH;
            var label = pages > 1 ? $"{footer}   Page {i + 1} of {pages}" : footer;
            var content = new StringBuilder()
                .Append("q ").Append(N(Margin)).Append(' ').Append(N(clipY)).Append(' ').Append(N(contentW)).Append(' ').Append(N(contentH)).Append(" re W n\n")
                .Append("q ").Append(N(displayW)).Append(" 0 0 ").Append(N(displayH)).Append(' ').Append(N(x0)).Append(' ').Append(N(imageBottom)).Append(" cm /Im1 Do Q\n")
                .Append("Q\n")
                .Append("BT /F1 8 Tf 0.45 g ").Append(N(Margin)).Append(' ').Append(N(Margin)).Append(" Td (").Append(Escape(label)).Append(") Tj ET\n")
                .ToString();
            var stream = Encoding.Latin1.GetBytes(content);
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageW)} {N(PageH)}] " +
                              $"/Resources << /XObject << /Im1 4 0 R >> /Font << /F1 3 0 R >> >> /Contents {6 + i * 2} 0 R >>"));
            objects.Add(Concat(Ascii($"<< /Length {stream.Length} >>\nstream\n"), stream, Ascii("\nendstream")));
        }

        using var ms = new MemoryStream();
        Write(ms, Ascii("%PDF-1.4\n%âãÏÓ\n"));
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write(ms, Ascii($"{i + 1} 0 obj\n"));
            Write(ms, objects[i]);
            Write(ms, Ascii("\nendobj\n"));
        }
        var xref = ms.Position;
        var sb = new StringBuilder().Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        Write(ms, Ascii(sb.ToString()));
        return ms.ToArray();
    }

    /// <summary>Reads width, height and colour components from a JPEG's start-of-frame marker.</summary>
    public static (int Width, int Height, int Components) JpegInfo(byte[] d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new DomainException("The scan isn't a JPEG image.");
        var i = 2;
        while (i + 4 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            var marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }                                   // fill byte
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; } // no length
            var len = (d[i + 2] << 8) | d[i + 3];
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof && i + 9 < d.Length)
            {
                var height = (d[i + 5] << 8) | d[i + 6];
                var width = (d[i + 7] << 8) | d[i + 8];
                var comps = d[i + 9];
                if (width > 0 && height > 0) return (width, height, comps);
            }
            i += 2 + len;
        }
        throw new DomainException("Couldn't read the scan's size.");
    }

    static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\').Append(c);
            else if (c is >= ' ' and <= '~') sb.Append(c);
            else if (c is >= ' ' and <= 'ÿ') sb.Append(c);                 // Latin-1, written as WinAnsi
            else sb.Append('?');
        }
        return sb.ToString();
    }

    static byte[] Ascii(string s) => Encoding.Latin1.GetBytes(s);
    static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    static void Write(Stream s, byte[] b) => s.Write(b, 0, b.Length);
}
