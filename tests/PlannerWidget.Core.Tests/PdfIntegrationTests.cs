using PlannerWidget.Core.Attendance;

namespace PlannerWidget.Core.Tests;

/// <summary>
/// Valódi PDF-en futó teszt. Csak akkor csinál bármit, ha a PLANNERWIDGET_TEST_PDF környezeti változó
/// egy jelenléti ívre mutat. A fájlt sosem módosítja: egy ideiglenes másolaton dolgozik.
/// </summary>
public class PdfIntegrationTests
{
    private static string? SourcePdf => Environment.GetEnvironmentVariable("PLANNERWIDGET_TEST_PDF");

    [Fact]
    public void Fill_real_attendance_sheet_copy()
    {
        if (SourcePdf is not { } source || !File.Exists(source))
        {
            return; // Nincs megadva tesztfájl – kihagyjuk.
        }

        var copy = Path.Combine(Path.GetTempPath(), $"pw-test-{Guid.NewGuid():N}.pdf");
        File.Copy(source, copy);
        try
        {
            var pdf = new PdfSharpFormService();
            var service = new AttendanceService(pdf);

            var names = service.GetFieldNames(copy);
            var templates = AttendanceFieldDetector.Detect(names, 14);
            Assert.NotNull(templates);

            var start = new DateTime(2026, 3, 14, 8, 50, 0);
            var end = new DateTime(2026, 3, 14, 16, 20, 0);
            var arrival = service.RecordArrival(copy, templates, start);
            var departure = service.RecordDeparture(copy, templates, start, end, "Teszt Elek");

            Assert.Equal("9:00", arrival.ClockText);
            Assert.Equal(7, departure.Hours);

            var after = pdf.ReadFields(copy);
            Assert.Equal("9:00", after[FieldTemplate.Resolve(templates.Arrival, 14)]);
            Assert.Equal("16:00", after[FieldTemplate.Resolve(templates.Leave, 14)]);
            Assert.Equal("7", after[FieldTemplate.Resolve(templates.Hours, 14)]);
            Assert.Equal("Teszt Elek", after[FieldTemplate.Resolve(templates.Signature, 14)]);
            if (templates.TotalHours is { } total)
            {
                Assert.Equal(departure.MonthlyTotal, after[total]);
            }
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public void Names_with_hungarian_double_acute_are_preserved()
    {
        if (SourcePdf is not { } source || !File.Exists(source))
        {
            return;
        }

        var copy = Path.Combine(Path.GetTempPath(), $"pw-test-{Guid.NewGuid():N}.pdf");
        File.Copy(source, copy);
        try
        {
            var pdf = new PdfSharpFormService();
            pdf.WriteFields(copy, new Dictionary<string, string> { ["ALÁÍRÁS21"] = "Kőrösi Ödön Űrös" });
            Assert.Equal("Kőrösi Ödön Űrös", pdf.ReadFields(copy)["ALÁÍRÁS21"]);
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
