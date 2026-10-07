using PlannerWidget.Core.Attendance;

namespace PlannerWidget.Core.Tests;

/// <summary>
/// A valódi jelenléti ív mezőszerkezetét másoló névlista
/// (a nappali óraszám 1–9: "60022001", 10-től: "600220010").
/// </summary>
public class AttendanceFieldTests
{
    private static readonly string[] RealLikeFieldNames = BuildFieldNames();

    private static string[] BuildFieldNames()
    {
        var names = new List<string> { "UWTELJES NÉV", "FOGLALKOZTATÓTELJES NÉV", "Azonosító", "ÉV", "HÓNAP" };
        for (var d = 1; d <= 31; d++)
        {
            names.Add($"ÉRKEZÉS IDEJE{d}");
            names.Add($"TÁVOZÁS IDEJE{d}");
            names.Add($"ÓRASZÁM NAPPAL 6002200{d}");
            names.Add($"ÓRASZÁM ÉJSZAKA 2200600{d}");
            names.Add($"ALÁÍRÁS{d}");
            names.Add($"fill_{2 + 6 * d}");
        }

        names.Add("ÓRASZÁM NAPPAL 6002200HAVI ÖSSZESÍTETT ÓRASZÁM");
        names.Add("ÓRASZÁM ÉJSZAKA 2200600HAVI ÖSSZESÍTETT ÓRASZÁM");
        return names.ToArray();
    }

    private static HashSet<string> NameSet => RealLikeFieldNames.ToHashSet(StringComparer.Ordinal);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(21)]
    [InlineData(31)]
    public void Detect_finds_templates_that_work_for_every_day(int detectionDay)
    {
        var templates = AttendanceFieldDetector.Detect(RealLikeFieldNames, detectionDay);

        Assert.NotNull(templates);
        Assert.Equal("ÉRKEZÉS IDEJE{day}", templates.Arrival);
        Assert.Equal("TÁVOZÁS IDEJE{day}", templates.Leave);
        Assert.Equal("ÓRASZÁM NAPPAL 6002200{day}", templates.Hours);
        Assert.Equal("ALÁÍRÁS{day}", templates.Signature);
        Assert.Equal("ÓRASZÁM NAPPAL 6002200HAVI ÖSSZESÍTETT ÓRASZÁM", templates.TotalHours);

        foreach (var template in templates.DailyTemplates)
        {
            Assert.Equal(31, FieldTemplate.Score(template, NameSet));
        }
    }

    [Fact]
    public void Repair_fixes_the_buggy_python_template()
    {
        // A régi verzió március 3-án ezt mentette el, ami 10-e után nem létező mezőt ad.
        const string legacy = "ÓRASZÁM NAPPAL 600220{day:02d}";
        Assert.Equal("ÓRASZÁM NAPPAL 60022010", FieldTemplate.Resolve(legacy, 10));
        Assert.Equal(9, FieldTemplate.Score(legacy, NameSet));

        var repaired = FieldTemplate.Repair(legacy, NameSet);

        Assert.Equal("ÓRASZÁM NAPPAL 6002200{day}", repaired);
        Assert.Equal("ÓRASZÁM NAPPAL 600220010", FieldTemplate.Resolve(repaired, 10));
    }

    [Fact]
    public void Repair_keeps_good_templates()
    {
        Assert.Equal("ÉRKEZÉS IDEJE{day}", FieldTemplate.Repair("ÉRKEZÉS IDEJE{day}", NameSet));
    }

    [Fact]
    public void Resolve_supports_python_placeholders()
    {
        Assert.Equal("X7", FieldTemplate.Resolve("X{day}", 7));
        Assert.Equal("X07", FieldTemplate.Resolve("X{day:02d}", 7));
        Assert.Equal("X12", FieldTemplate.Resolve("X{day:02d}", 12));
    }

    [Fact]
    public void Detect_returns_null_for_unrelated_form()
    {
        Assert.Null(AttendanceFieldDetector.Detect(["Name", "Address", "City"], 5));
    }

    [Fact]
    public void Repair_updates_whole_template_set()
    {
        var legacy = new AttendanceTemplates(
            "ÉRKEZÉS IDEJE{day}", "TÁVOZÁS IDEJE{day}", "ÓRASZÁM NAPPAL 600220{day:02d}", "ALÁÍRÁS{day}",
            "ÓRASZÁM NAPPAL 6002200HAVI ÖSSZESÍTETT ÓRASZÁM");

        var repaired = AttendanceService.Repair(legacy, NameSet);

        Assert.Equal("ÓRASZÁM NAPPAL 6002200{day}", repaired.Hours);
        Assert.Equal(legacy.Arrival, repaired.Arrival);
        Assert.Equal(legacy.TotalHours, repaired.TotalHours);
    }
}
