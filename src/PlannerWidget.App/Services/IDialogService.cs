using PlannerWidget.Core.Models;

namespace PlannerWidget.App.Services;

public enum DialogChoice
{
    None,
    Primary,
    Secondary,
}

public sealed record TaskEditModel(string Title, DateOnly? DueDate, TaskPriority Priority);

/// <summary>Felhasználói párbeszédek – a nézetmodell így nem függ közvetlenül a XAML-től.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string primaryText, bool destructive = false);

    Task<DialogChoice> ChooseAsync(string title, string message, string primaryText, string? secondaryText, string closeText = "Mégse");

    Task ShowMessageAsync(string title, string message);

    Task<TaskEditModel?> EditTaskAsync(TaskEditModel current);

    /// <summary>Mezőválasztó kereséssel. Null, ha a felhasználó megszakította (vagy kihagyta, ha opcionális).</summary>
    Task<string?> PickFieldAsync(string title, string instruction, IReadOnlyList<string> fieldNames, bool optional);

    Task<string?> PickPdfAsync();

    Task<string?> PickFolderAsync();
}
