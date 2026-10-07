using System.Globalization;
using System.Text.RegularExpressions;
using PlannerWidget.Core.Models;

namespace PlannerWidget.Core.Tickets;

/// <summary>
/// Egy „Beérkező ügyek” ticket a Planner-feladat szemszögéből. Az <see cref="Id"/> a SharePoint-listaelem
/// azonosítója; null, ha a cím nem a „[#123] Tárgy” mintával kezdődik (ilyenkor nincs link).
/// </summary>
public sealed record TicketInfo(int? Id);

/// <summary>
/// Ügyfelismerés. A Power Automate flow-k a ticketeket egy külön tervbe teszik ki „[#&lt;ID&gt;] &lt;Tárgy&gt;” címmel;
/// ügy minden feladat, amely a beállított ügytervben van.
/// </summary>
public static partial class TicketParser
{
    [GeneratedRegex(@"^\s*\[\s*#\s*(?<id>[^\]]*?)\s*\]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();

    /// <summary>Null, ha nincs ügyterv beállítva, vagy a feladat nem abban van.</summary>
    public static TicketInfo? TryGet(PlannerTask task, string? ticketPlanId)
    {
        if (string.IsNullOrWhiteSpace(ticketPlanId) || task.PlanId != ticketPlanId)
        {
            return null;
        }

        return new TicketInfo(ParseId(task.Title));
    }

    /// <summary>A cím elején álló „[#123]” szám része; null, ha a minta hiányzik vagy nem (érvényes) szám.</summary>
    public static int? ParseId(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var match = PrefixRegex().Match(title);
        if (!match.Success)
        {
            return null;
        }

        var text = match.Groups["id"].Value;
        return text.Length > 0 && text.All(char.IsAsciiDigit)
               && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }

    /// <summary>A cím a „[#123] ” előtag nélkül (a kártyán a jelvény mutatja az azonosítót).</summary>
    public static string StripPrefix(string title)
    {
        if (ParseId(title) is null)
        {
            return title;
        }

        var rest = title[PrefixRegex().Match(title).Length..].Trim();
        return rest.Length > 0 ? rest : title;
    }
}

/// <summary>A ticket megnyitási linkje egy beállítható sablonból („{id}” helyőrzővel).</summary>
public static class TicketLinks
{
    public const string Placeholder = "{id}";

    public const string DefaultUrlTemplate =
        "https://laesze.sharepoint.com/sites/MFI-RFK/Lists/Berkez%20gyek/DispForm.aspx?ID=" + Placeholder;

    /// <summary>Érvényes-e a sablon: van benne „{id}”, és behelyettesítve abszolút http(s) cím.</summary>
    public static bool IsValidTemplate(string? template) => Build(template, 1) is not null;

    /// <summary>Null, ha a sablon üres, nincs benne „{id}”, vagy nem ad érvényes http(s) címet.</summary>
    public static Uri? Build(string? template, int id)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains(Placeholder, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = template.Trim().Replace(Placeholder, id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
    }
}
