using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;

namespace PlannerWidget.Core.Attendance;

/// <summary>
/// Szövegmező megjelenésének (appearance stream) megrajzolása pontosan az űrlap saját
/// beállításaival: a mező /DA betűje és mérete, a /Q igazítás, az AcroForm /DR betűkészlete.
/// Így a kitöltött ív ugyanúgy néz ki, mintha Acrobatban gépelték volna be
/// (a PDFsharp beépített rajzolása 10 pt-os, balra zárt Arialt használna).
/// </summary>
internal static partial class TextFieldAppearance
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Megpróbálja megrajzolni; hamis, ha a mező vagy a szöveg ezt nem teszi lehetővé.</summary>
    public static bool TryWrite(PdfDocument document, PdfAcroForm form, PdfTextField field, string value)
    {
        if (!IsLatin1(value))
        {
            return false; // Pl. ő/ű – a mező Type1 Helvetica betűje (WinAnsi) ezeket nem tartalmazza.
        }

        var da = field.Elements.GetString("/DA");
        if (string.IsNullOrEmpty(da))
        {
            da = form.Elements.GetString("/DA");
        }

        var match = DaFontRegex().Match(da ?? "");
        if (!match.Success)
        {
            return false;
        }

        var fontKey = match.Groups["font"].Value;
        var fontRef = form.Elements.GetDictionary("/DR")?.Elements.GetDictionary("/Font")?.Elements["/" + fontKey];
        if (fontRef is null)
        {
            return false;
        }

        var widgets = Widgets(field).ToList();
        if (widgets.Count == 0)
        {
            return false;
        }

        var requestedSize = double.Parse(match.Groups["size"].Value, Inv);
        var colorOps = DaFontRegex().Replace(da!, "").Trim();
        var alignment = field.Elements.GetInteger("/Q");

        foreach (var widget in widgets)
        {
            var rect = widget.Elements.GetRectangle("/Rect");
            var width = Math.Abs(rect.Width);
            var height = Math.Abs(rect.Height);
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var size = requestedSize > 0 ? requestedSize : AutoSize(value, width, height);
            var textWidth = Measure(value, size);
            var x = alignment switch
            {
                1 => (width - textWidth) / 2,
                2 => width - 2 - textWidth,
                _ => 2,
            };
            var y = (height - size * 0.742) / 2;

            var content = new StringBuilder()
                .Append("/Tx BMC\nq\n")
                .Append(Inv, $"0 0 {N(width)} {N(height)} re\nW\nn\nBT\n")
                .Append(Inv, $"/{fontKey} {N(size)} Tf\n")
                .Append(string.IsNullOrEmpty(colorOps) ? "0 g" : colorOps).Append('\n')
                .Append(Inv, $"{N(Math.Max(x, 1))} {N(y)} Td\n")
                .Append('(').Append(EscapeLiteral(value)).Append(") Tj\nET\nQ\nEMC\n")
                .ToString();

            var fonts = new PdfDictionary(document);
            fonts.Elements["/" + fontKey] = fontRef;
            var resources = new PdfDictionary(document);
            resources.Elements["/Font"] = fonts;

            var xobject = new PdfDictionary(document);
            xobject.Elements.SetName("/Type", "/XObject");
            xobject.Elements.SetName("/Subtype", "/Form");
            xobject.Elements["/BBox"] = new PdfArray(document, new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height));
            xobject.Elements["/Resources"] = resources;
            xobject.CreateStream(Encoding.ASCII.GetBytes(content));
            document.Internals.AddObject(xobject);

            var appearance = new PdfDictionary(document);
            appearance.Elements.SetReference("/N", xobject);
            widget.Elements["/AP"] = appearance;
        }

        return true;
    }

    private static IEnumerable<PdfDictionary> Widgets(PdfAcroField field)
    {
        if (field.Elements.ContainsKey("/Rect"))
        {
            yield return field;
            yield break;
        }

        if (field.Elements.GetArray("/Kids") is { } kids)
        {
            for (var i = 0; i < kids.Elements.Count; i++)
            {
                if (kids.Elements.GetDictionary(i) is { } kid && kid.Elements.ContainsKey("/Rect"))
                {
                    yield return kid;
                }
            }
        }
    }

    private static double AutoSize(string text, double width, double height)
    {
        var size = Math.Min(12, height * 0.7);
        while (size > 4 && Measure(text, size) > width - 4)
        {
            size -= 0.5;
        }

        return size;
    }

    // Az Arial metrikusan azonos a Helveticával, ezért ezzel mérünk.
    private static double Measure(string text, double size)
    {
        var gfx = XGraphics.CreateMeasureContext(new XSize(1000, 1000), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", size)).Width;
    }

    private static bool IsLatin1(string value) => value.All(c => c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ');

    private static string EscapeLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\' or '(' or ')':
                    sb.Append('\\').Append(c);
                    break;
                case > '~':
                    sb.Append('\\').Append(Convert.ToString(c, 8).PadLeft(3, '0'));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string N(double value) => value.ToString("0.###", Inv);

    [GeneratedRegex(@"/(?<font>[^\s/]+)\s+(?<size>[\d.]+)\s+Tf")]
    private static partial Regex DaFontRegex();
}
