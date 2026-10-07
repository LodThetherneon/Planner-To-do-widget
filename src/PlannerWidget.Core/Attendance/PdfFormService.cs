using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using PlannerWidget.Core.Logging;

namespace PlannerWidget.Core.Attendance;

public interface IPdfFormService
{
    /// <summary>Az összes kitölthető mező neve és aktuális értéke.</summary>
    IReadOnlyDictionary<string, string> ReadFields(string path);

    /// <summary>Mezők kitöltése. A fájlt atomikusan cseréli (ideiglenes fájl + átnevezés).</summary>
    void WriteFields(string path, IReadOnlyDictionary<string, string> values);
}

public class AttendanceException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PdfFileLockedException(string path, Exception inner)
    : AttendanceException($"A PDF nyitva van egy másik programban, zárd be és próbáld újra:\n{Path.GetFileName(path)}", inner);

/// <summary>AcroForm kitöltés PDFsharp-pal (MIT licenc).</summary>
public sealed class PdfSharpFormService : IPdfFormService
{
    private static readonly Lock FontLock = new();

    public PdfSharpFormService()
    {
        lock (FontLock)
        {
            // A PDF mezők "Helvetica" betűt kérnek; ezt a Windows Arial fájljaira képezzük le,
            // hogy a PDFsharp meg tudja rajzolni a mezők megjelenését.
            GlobalFontSettings.FontResolver ??= new WindowsArialFontResolver();
        }
    }

    public IReadOnlyDictionary<string, string> ReadFields(string path)
    {
        EnsureExists(path);
        try
        {
            using var document = Open(path, PdfDocumentOpenMode.Import);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (GetForm(document) is { } form)
            {
                foreach (var (name, field) in EnumerateFields(form.Fields, prefix: null))
                {
                    result[name] = ReadValue(field);
                }
            }

            return result;
        }
        catch (IOException ex) when (IsLocked(ex))
        {
            throw new PdfFileLockedException(path, ex);
        }
        catch (Exception ex) when (ex is not AttendanceException)
        {
            throw new AttendanceException($"A PDF nem olvasható: {ex.Message}", ex);
        }
    }

    public void WriteFields(string path, IReadOnlyDictionary<string, string> values)
    {
        EnsureExists(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var document = Open(path, PdfDocumentOpenMode.Modify))
            {
                var form = GetForm(document) ?? throw new AttendanceException("A PDF-ben nincs kitölthető űrlap.");
                var fields = EnumerateFields(form.Fields, prefix: null).ToDictionary(f => f.Name, f => f.Field, StringComparer.Ordinal);

                var missing = values.Keys.Where(k => !fields.ContainsKey(k)).ToList();
                if (missing.Count > 0)
                {
                    throw new AttendanceException("Nem található mező a PDF-ben: " + string.Join(", ", missing));
                }

                var allExact = true;
                foreach (var (name, value) in values)
                {
                    allExact &= SetValue(document, form, fields[name], value);
                }

                if (!allExact)
                {
                    // Tartalék rajzolás történt: kérjük a PDF-nézőt, hogy a mentett értékekből rajzolja újra.
                    form.Elements.SetBoolean("/NeedAppearances", true);
                }

                document.Save(temp);
            }

            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Olvasni tudtuk, cserélni nem: tipikusan egy PDF-néző tartja nyitva.
                throw new PdfFileLockedException(path, ex);
            }
        }
        catch (IOException ex) when (IsLocked(ex))
        {
            throw new PdfFileLockedException(path, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AttendanceException($"Nincs írási jog a PDF-hez: {path}", ex);
        }
        catch (Exception ex) when (ex is not AttendanceException)
        {
            throw new AttendanceException($"A PDF kitöltése nem sikerült: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>
    /// Megnyitás, sérült fájlnál javítással. Tipikus hiba: egy rövidebb mentés csonkolás nélkül írta felül a régebbi,
    /// hosszabb fájlt, így az új %%EOF után a régi változat vége maradt ott, rossz startxref-fel. Ilyenkor a fájlt
    /// egy korábbi %%EOF-nál elvágva (az utolsó ép változatnál) nyitjuk meg; íráskor a mentés már ép fájlt ad.
    /// </summary>
    private static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException))
        {
            var bytes = File.ReadAllBytes(path);
            foreach (var end in RevisionEnds(bytes).Where(e => e < bytes.Length))
            {
                try
                {
                    var document = PdfReader.Open(new MemoryStream(bytes, 0, end, writable: false), mode);
                    Log.Warn($"Sérült PDF ({ex.Message.Split('\n')[0].Trim()}): az utolsó ép változatot használjuk " +
                             $"({end} / {bytes.Length} bájt): {path}");
                    return document;
                }
                catch (Exception)
                {
                    // Ez a változat sem ép – próbáljuk a korábbit.
                }
            }

            throw;
        }
    }

    // A PDFsharp AcroForm tulajdonsága űrlap nélküli PDF-nél kivételt dob null helyett.
    private static PdfAcroForm? GetForm(PdfDocument document) =>
        document.Internals.Catalog.Elements.ContainsKey("/AcroForm") ? document.AcroForm : null;

    /// <summary>A "%%EOF" jelölők (plusz sorvég) utáni pozíciók, hátulról előre.</summary>
    private static IEnumerable<int> RevisionEnds(byte[] bytes)
    {
        var marker = "%%EOF"u8.ToArray();
        var ends = new List<int>();
        var start = 0;
        int at;
        while ((at = bytes.AsSpan(start).IndexOf(marker)) >= 0)
        {
            var end = start + at + marker.Length;
            start = end;
            while (end < bytes.Length && bytes[end] is (byte)'\r' or (byte)'\n')
            {
                end++;
            }

            ends.Add(end);
        }

        ends.Reverse();
        return ends;
    }

    private static IEnumerable<(string Name, PdfAcroField Field)> EnumerateFields(PdfAcroField.PdfAcroFieldCollection fields, string? prefix)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var partial = field.Name ?? "";
            var name = string.IsNullOrEmpty(prefix) ? partial : string.IsNullOrEmpty(partial) ? prefix : $"{prefix}.{partial}";

            if (field.HasKids && field.Fields.Count > 0 && HasNamedKids(field.Fields))
            {
                foreach (var child in EnumerateFields(field.Fields, name))
                {
                    yield return child;
                }
            }
            else if (!string.IsNullOrEmpty(name))
            {
                yield return (name, field);
            }
        }
    }

    // A névtelen gyerekek csak a mező megjelenési (widget) példányai, nem külön mezők.
    private static bool HasNamedKids(PdfAcroField.PdfAcroFieldCollection kids)
    {
        for (var i = 0; i < kids.Count; i++)
        {
            if (!string.IsNullOrEmpty(kids[i].Name))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadValue(PdfAcroField field)
    {
        if (field is PdfTextField text)
        {
            return text.Text ?? "";
        }

        return field.Value switch
        {
            PdfString s => s.Value,
            PdfName n => n.Value.TrimStart('/'),
            null => "",
            var other => other.ToString() ?? "",
        };
    }

    /// <returns>Igaz, ha a megjelenés pontosan az űrlap saját betűjével készült.</returns>
    private static bool SetValue(PdfDocument document, PdfAcroForm form, PdfAcroField field, string value)
    {
        if (field.ReadOnly)
        {
            throw new AttendanceException($"A mező csak olvasható: {field.Name}");
        }

        var latin1 = value.All(c => c <= 'ÿ');
        field.Elements["/V"] = new PdfString(value, latin1 ? PdfStringEncoding.PDFDocEncoding : PdfStringEncoding.Unicode);

        if (field is not PdfTextField text)
        {
            return false;
        }

        // 1. Saját megjelenítés az űrlap betűjével és igazításával (az eredetivel azonos kinézet).
        if (TextFieldAppearance.TryWrite(document, form, text, value))
        {
            return true;
        }

        // 2. Tartalék: a PDFsharp saját rajzolása (Unicode betűvel, pl. ő/ű esetén).
        try
        {
            text.Text = value;
        }
        catch (Exception)
        {
            // Ha ez sem sikerül, marad az érték: a NeedAppearances jelző miatt a PDF-néző kirajzolja.
        }

        return false;
    }

    private static void EnsureExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new AttendanceException($"A PDF fájl nem található:\n{path}");
        }
    }

    private static bool IsLocked(IOException ex) =>
        (ex.HResult & 0xFFFF) is 32 or 33; // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class WindowsArialFontResolver : IFontResolver
    {
        private static readonly string FontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new((bold, italic) switch
            {
                (true, true) => "arialbi",
                (true, false) => "arialbd",
                (false, true) => "ariali",
                _ => "arial",
            });

        public byte[]? GetFont(string faceName)
        {
            var file = Path.Combine(FontsFolder, faceName + ".ttf");
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
    }
}
