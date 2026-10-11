using System.Globalization;
using System.Text;

namespace XtremScan.Scanning;

/// <summary>
/// PDF 1.4 d'une ou plusieurs pages. Chaque page embarque son JPEG, recadré et tourné sans le réencoder.
/// </summary>
public static class PdfDocumentWriter
{
    public static void Write(string pdfPath, IReadOnlyList<PdfPageSpec> pages)
    {
        if (pages.Count == 0)
        {
            throw new InvalidOperationException("Le document ne contient aucune page.");
        }

        var pictures = pages.Select(page => JpegPicture.Read(page.JpegPath)).ToArray();
        var placements = new PdfImagePlacement[pages.Count];
        for (var index = 0; index < pages.Count; index++)
        {
            placements[index] = PdfPagePlacement.Place(pictures[index], pages[index]);
        }

        var objectCount = 3 + (pages.Count * 3);
        var xref = new long[objectCount + 1];
        using var output = new FileStream(pdfPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteAscii(output, "%PDF-1.4\n%\n");
        xref[1] = output.Position;
        WriteAscii(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        xref[2] = output.Position;
        WriteAscii(output, "2 0 obj\n<< /Type /Pages /Count " + pages.Count.ToString(CultureInfo.InvariantCulture) + " /Kids [");
        for (var index = 0; index < pages.Count; index++)
        {
            if (index > 0)
            {
                WriteAscii(output, " ");
            }

            WriteAscii(output, PageObject(index).ToString(CultureInfo.InvariantCulture) + " 0 R");
        }

        WriteAscii(output, "] >>\nendobj\n");
        xref[3] = output.Position;
        WriteAscii(output, "3 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = PageObject(index);
            var contentId = pageId + 1;
            var imageId = pageId + 2;
            var picture = pictures[index];
            var placement = placements[index];
            var colorSpace = picture.Components == 1 ? "/DeviceGray" : "/DeviceRGB";
            var contents = Content(placement, pages[index]);
            xref[pageId] = output.Position;
            WriteAscii(
                output,
                pageId.ToString(CultureInfo.InvariantCulture)
                + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "
                + Format(placement.PageWidth) + " " + Format(placement.PageHeight)
                + "] /Resources << /XObject << /Im " + imageId.ToString(CultureInfo.InvariantCulture)
                + " 0 R >> /Font << /F1 3 0 R >> >> /Contents "
                + contentId.ToString(CultureInfo.InvariantCulture) + " 0 R >>\nendobj\n");
            xref[contentId] = output.Position;
            WriteAscii(
                output,
                contentId.ToString(CultureInfo.InvariantCulture)
                + " 0 obj\n<< /Length " + contents.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n");
            output.Write(contents);
            WriteAscii(output, "\nendstream\nendobj\n");
            var jpegLength = new FileInfo(pages[index].JpegPath).Length;
            xref[imageId] = output.Position;
            WriteAscii(
                output,
                imageId.ToString(CultureInfo.InvariantCulture)
                + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + picture.Width.ToString(CultureInfo.InvariantCulture)
                + " /Height " + picture.Height.ToString(CultureInfo.InvariantCulture)
                + " /ColorSpace " + colorSpace
                + " /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpegLength.ToString(CultureInfo.InvariantCulture)
                + " >>\nstream\n");
            using (var jpeg = File.OpenRead(pages[index].JpegPath))
            {
                jpeg.CopyTo(output);
            }

            WriteAscii(output, "\nendstream\nendobj\n");
        }

        var startXref = output.Position;
        WriteAscii(output, "xref\n0 " + (objectCount + 1).ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
        for (var index = 1; index <= objectCount; index++)
        {
            WriteAscii(output, xref[index].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }

        WriteAscii(
            output,
            "trailer\n<< /Size " + (objectCount + 1).ToString(CultureInfo.InvariantCulture)
            + " /Root 1 0 R >>\nstartxref\n"
            + startXref.ToString(CultureInfo.InvariantCulture)
            + "\n%%EOF\n");
    }

    private static int PageObject(int index) => 4 + (index * 3);

    private static byte[] Content(PdfImagePlacement placement, PdfPageSpec page)
    {
        var text = page.Text;
        var zoom = page.Zoom < 0.05 ? 1 : page.Zoom;
        var shiftX = page.ImageOffsetX * placement.PageWidth;
        var shiftY = -page.ImageOffsetY * placement.PageHeight;
        var builder = new StringBuilder();
        AppendImage(builder, placement, null, 0, zoom, double.NaN, double.NaN, shiftX, shiftY);

        if (text.Count > 0)
        {
            builder.Append("BT\n3 Tr\n");
            foreach (var run in text)
            {
                if (string.IsNullOrWhiteSpace(run.Text) || run.Size <= 0)
                {
                    continue;
                }

                builder.Append("/F1 ");
                builder.Append(Format(run.Size));
                builder.Append(" Tf\n1 0 0 1 ");
                builder.Append(Format(run.X + shiftX));
                builder.Append(' ');
                builder.Append(Format(run.Baseline + shiftY));
                builder.Append(" Tm\n(");
                builder.Append(WinAnsi.Escape(run.Text));
                builder.Append(") Tj\n");
            }

            builder.Append("ET\n");
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void AppendImage(
        StringBuilder builder,
        PdfImagePlacement placement,
        PageFrame? frame,
        int turns,
        double zoom,
        double anchorX = double.NaN,
        double anchorY = double.NaN,
        double shiftX = 0,
        double shiftY = 0)
    {
        builder.Append("q\n");
        if (frame is PageFrame selection)
        {
            var clip = selection.PdfRect(placement.PageWidth, placement.PageHeight);
            builder.Append(Format(clip.X));
            builder.Append(' ');
            builder.Append(Format(clip.Y));
            builder.Append(' ');
            builder.Append(Format(clip.Width));
            builder.Append(' ');
            builder.Append(Format(clip.Height));
            builder.Append(" re\nW\nn\n");
            var anchorPageX = double.IsNaN(anchorX) ? selection.Center.X : anchorX;
            var anchorPageY = double.IsNaN(anchorY) ? selection.Center.Y : anchorY;
            var spin = selection.PdfSpin(placement.PageWidth, placement.PageHeight, turns, zoom, anchorPageX, anchorPageY);
            builder.Append(Format(spin.A));
            builder.Append(' ');
            builder.Append(Format(spin.B));
            builder.Append(' ');
            builder.Append(Format(spin.C));
            builder.Append(' ');
            builder.Append(Format(spin.D));
            builder.Append(' ');
            builder.Append(Format(spin.E + shiftX));
            builder.Append(' ');
            builder.Append(Format(spin.F + shiftY));
            builder.Append(" cm\n");
        }
        else
        {
            builder.Append("0 0 ");
            builder.Append(Format(placement.PageWidth));
            builder.Append(' ');
            builder.Append(Format(placement.PageHeight));
            builder.Append(" re\nW\nn\n");
        }

        builder.Append(Format(placement.A * zoom));
        builder.Append(' ');
        builder.Append(Format(placement.B * zoom));
        builder.Append(' ');
        builder.Append(Format(placement.C * zoom));
        builder.Append(' ');
        builder.Append(Format(placement.D * zoom));
        builder.Append(' ');
        var centerX = placement.PageWidth / 2d;
        var centerY = placement.PageHeight / 2d;
        builder.Append(Format(centerX + ((placement.E - centerX) * zoom) + shiftX));
        builder.Append(' ');
        builder.Append(Format(centerY + ((placement.F - centerY) * zoom) + shiftY));
        builder.Append(" cm\n/Im Do\nQ\n");
    }

    private static void WriteAscii(Stream output, string text)
    {
        output.Write(Encoding.ASCII.GetBytes(text));
    }

    private static string Format(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
