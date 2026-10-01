using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;

namespace PlannerWidget.App.ViewModels;

/// <summary>Amit egy feladatkártya a fő nézetmodelltől kér.</summary>
public interface ITaskActions
{
    Task ToggleCompleteAsync(TaskItemViewModel item);
    Task DeleteAsync(TaskItemViewModel item);
    Task EditAsync(TaskItemViewModel item);
    Task ToggleDetailsAsync(TaskItemViewModel item);
    Task SetChecklistItemAsync(TaskItemViewModel item, ChecklistItemViewModel checklistItem);
    void OpenInPlanner(TaskItemViewModel item);
    string? GetPlanTitle(string planId);
}

public sealed partial class TaskItemViewModel : ObservableObject
{
    private readonly ITaskActions _actions;

    public TaskItemViewModel(PlannerTask task, ITaskActions actions)
    {
        _actions = actions;
        Model = task;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsCompleted), nameof(DueText), nameof(DueState), nameof(PlanTitle),
        nameof(Priority), nameof(PriorityText), nameof(ChecklistText), nameof(HasChecklist), nameof(HasDescription),
        nameof(MetaText), nameof(CheckToolTip), nameof(DueToolTip), nameof(HasNoDetails))]
    public partial PlannerTask Model { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingDetails { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescriptionText))]
    public partial string Description { get; set; } = "";

    public PlannerTaskDetails? Details { get; private set; }

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; } = [];

    public string Id => Model.Id;
    public string Title => Model.Title;
    public bool IsCompleted => Model.IsCompleted;
    public DueState DueState => TaskOrdering.GetDueState(Model, Today);
    public TaskPriority Priority => Model.PriorityLevel;
    public string PriorityText => Model.PriorityLevel.ToDisplayName();
    public string PlanTitle => _actions.GetPlanTitle(Model.PlanId) ?? "";
    public bool HasChecklist => Model.ChecklistItemCount > 0;
    public string ChecklistText => $"{Model.CompletedChecklistItemCount}/{Model.ChecklistItemCount}";
    public bool HasDescription => Model.HasDescription;
    public bool HasDescriptionText => !string.IsNullOrWhiteSpace(Description);
    public bool HasNoDetails => !Model.HasDescription && Model.ChecklistItemCount == 0;
    public string CheckToolTip => IsCompleted ? "Visszanyitás" : "Késznek jelölés";

    public string DueText => IsCompleted
        ? Model.CompletedDateTime is { } done
            ? "Kész · " + DueDateFormatter.FormatDate(DateOnly.FromDateTime(done.LocalDateTime), Today)
            : "Kész"
        : DueDateFormatter.Format(Model.DueDate, Today);

    public string DueToolTip => Model.DueDate is { } d ? "Határidő: " + DueDateFormatter.FormatLong(d) : "Nincs határidő";

    /// <summary>Képernyőolvasónak és a kompakt nézetnek: „Ma · BKR · Sürgős”.</summary>
    public string MetaText => string.Join(" · ", new[] { DueText, PlanTitle }.Where(s => !string.IsNullOrEmpty(s)));

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public void Update(PlannerTask task)
    {
        if (!ReferenceEquals(Model, task))
        {
            Model = task;
        }
    }

    /// <summary>Napváltáskor a relatív határidő-szövegek frissítése.</summary>
    public void RefreshComputed()
    {
        OnPropertyChanged(nameof(DueText));
        OnPropertyChanged(nameof(DueState));
        OnPropertyChanged(nameof(MetaText));
    }

    public void ApplyDetails(PlannerTaskDetails details)
    {
        Details = details;
        Description = details.Description.Trim();

        Checklist.Clear();
        foreach (var item in details.Checklist)
        {
            Checklist.Add(new ChecklistItemViewModel(this, item, _actions));
        }

        var active = details.Checklist.Count(c => !c.IsChecked);
        if (Model.ChecklistItemCount != details.Checklist.Count || Model.ActiveChecklistItemCount != active)
        {
            Model = Model with { ChecklistItemCount = details.Checklist.Count, ActiveChecklistItemCount = active };
        }
    }

    [RelayCommand]
    private Task ToggleComplete() => _actions.ToggleCompleteAsync(this);

    [RelayCommand]
    private Task Delete() => _actions.DeleteAsync(this);

    [RelayCommand]
    private Task Edit() => _actions.EditAsync(this);

    [RelayCommand]
    private Task ToggleDetails() => _actions.ToggleDetailsAsync(this);

    [RelayCommand]
    private void Open() => _actions.OpenInPlanner(this);

    /// <summary>A kártya szövegrészére koppintás: részletek ki/be.</summary>
    public void OnTapped() => ToggleDetailsCommand.Execute(null);
}

public sealed partial class ChecklistItemViewModel : ObservableObject
{
    private readonly TaskItemViewModel _owner;
    private readonly ITaskActions _actions;
    private bool _suppress;

    public ChecklistItemViewModel(TaskItemViewModel owner, ChecklistItem item, ITaskActions actions)
    {
        _owner = owner;
        _actions = actions;
        Id = item.Id;
        Title = item.Title;
        _suppress = true;
        IsChecked = item.IsChecked;
        _suppress = false;
    }

    public string Id { get; }
    public string Title { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Szerveroldali hiba esetén visszaállítás, újabb hívás indítása nélkül.</summary>
    public void Revert(bool value)
    {
        _suppress = true;
        IsChecked = value;
        _suppress = false;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_suppress)
        {
            _ = _actions.SetChecklistItemAsync(_owner, this);
        }
    }
}
