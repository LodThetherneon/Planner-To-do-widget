using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;
using PlannerWidget.Core.Tickets;

namespace PlannerWidget.App.ViewModels;

/// <summary>Amit egy feladatkártya a fő nézetmodelltől kér.</summary>
public interface ITaskActions
{
    Task ToggleCompleteAsync(TaskItemViewModel item);
    Task DeleteAsync(TaskItemViewModel item);
    Task EditAsync(TaskItemViewModel item);
    Task ToggleDetailsAsync(TaskItemViewModel item);
    Task SetChecklistItemAsync(TaskItemViewModel item, ChecklistItemViewModel checklistItem);
    Task RenameChecklistItemAsync(TaskItemViewModel item, ChecklistItemViewModel checklistItem, string title);
    void OpenInPlanner(TaskItemViewModel item);
    void OpenTicket(TaskItemViewModel item);

    /// <summary>Ügynél a felelőst a Beérkező ügyek listában kell módosítani – ezt magyarázza el.</summary>
    Task ExplainTicketAssigneeAsync(TaskItemViewModel item);
    string? GetPlanTitle(string planId);

    /// <summary>Ügy-e a feladat (a beállított ügytervben van); null, ha nem.</summary>
    TicketInfo? GetTicket(PlannerTask task);

    Uri? GetTicketUri(TicketInfo ticket);

    /// <summary>Az ügy státusza = a gyűjtő (bucket) neve az ügytervben; null, ha még nem ismert.</summary>
    string? GetTicketStatus(PlannerTask task);

    /// <summary>A „…” menü: áthelyezés a megadott állapot gyűjtőjébe.</summary>
    Task SetTicketStatusAsync(TaskItemViewModel item, string status);
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
        nameof(MetaText), nameof(CheckToolTip), nameof(DueToolTip), nameof(HasNoDetails), nameof(Ticket), nameof(IsTicket),
        nameof(TicketToolTip), nameof(HasTicketLink), nameof(DisplayTitle), nameof(CanDelete),
        nameof(TicketStatus), nameof(HasTicketStatus), nameof(NextTicketAction), nameof(CanToggleComplete), nameof(CheckEnabled),
        nameof(HasTicketMenu), nameof(CanSetQuestionable), nameof(CanSetSos), nameof(CanSetNew), nameof(ShowNewReply),
        nameof(TicketNumberText), nameof(HasTicketNumber), nameof(ShowPlainCheck), nameof(ShowTicketCheck), nameof(TicketCheckEnabled),
        nameof(ShowTakeOver), nameof(ShowStart), nameof(ShowResume), nameof(CanFinishTicket), nameof(FinishTicketText),
        nameof(TicketMenuHeader), nameof(ShowPlanTitle))]
    public partial PlannerTask Model { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckEnabled), nameof(TicketCheckEnabled))]
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

    /// <summary>A kártyán látható cím: ügynél a „[#123] ” előtag nélkül (azt a jelvény mutatja).</summary>
    public string DisplayTitle => IsTicket ? TicketParser.StripPrefix(Model.Title) : Model.Title;

    // Beérkező ügyek
    public TicketInfo? Ticket => _actions.GetTicket(Model);
    public bool IsTicket => Ticket is not null;
    public bool HasTicketLink => Ticket is { } t && _actions.GetTicketUri(t) is not null;

    /// <summary>
    /// Ügyet a widgetből nem törlünk: a Planner-feladatot a folyamat kezeli (felelős nélkül törli, felelőssel újra
    /// létrehozza), a Plannerben végzett változás nem jut vissza a Beérkező ügyek listába.
    /// </summary>
    public bool CanDelete => !IsTicket;

    public string TicketToolTip => Ticket?.Id is { } id
        ? $"Beérkező ügy (SharePoint #{id})"
        : "Beérkező ügy (a címben nincs „[#azonosító]”, ezért nincs link)";
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
    public string CheckToolTip => IsCompleted
        ? "Visszanyitás"
        : IsTicket
            ? TicketStatus is null
                ? "Az ügy állapota még nem töltődött be"
                : NextTicketAction is { } next ? $"Következő lépés: {next.ButtonText} (kattints a lehetőségekért)" : "Lehetőségek"
            : "Késznek jelölés";

    /// <summary>Ügyet a sima pipával is csak „In progress” állapotban lehet késznek jelölni; a visszanyitás mindig mehet.</summary>
    public bool CanToggleComplete => !IsTicket || IsCompleted || TicketWorkflow.CanComplete(TicketStatus);

    public bool CheckEnabled => !IsBusy && CanToggleComplete;

    // Ügy státusza (= Planner-gyűjtő) és a következő lépés
    public string? TicketStatus => IsTicket ? _actions.GetTicketStatus(Model) : null;
    public bool HasTicketStatus => TicketStatus is not null;
    public TicketAction? NextTicketAction => IsTicket ? TicketWorkflow.NextAction(TicketStatus, IsCompleted) : null;

    /// <summary>A rendszer újranyitotta (külsős válasz egy Done ügyre → Revisit): „Új válasz érkezett” jelölés.</summary>
    public bool ShowNewReply => IsTicket && TicketWorkflow.IsNewReply(TicketStatus, IsCompleted);

    /// <summary>A cím alatti kis „#123” (az „Ügy” jelvény helyett).</summary>
    public string TicketNumberText => Ticket?.Id is { } id ? $"#{id}" : "";
    public bool HasTicketNumber => TicketNumberText.Length > 0;

    /// <summary>Ügynél a terv neve fölösleges (a lila csempe jelzi), így kevésbé zsúfolt a kártya.</summary>
    public bool ShowPlanTitle => !IsTicket && !string.IsNullOrEmpty(PlanTitle);

    // A karika: sima feladatnál (és kész ügynél) kész/visszanyitás; nyitott ügynél a „továbblépés” menüt nyitja.
    public bool ShowPlainCheck => !IsTicket || IsCompleted;
    public bool ShowTicketCheck => IsTicket && !IsCompleted;
    public bool TicketCheckEnabled => !IsBusy && HasTicketStatus;
    public string TicketMenuHeader => $"Állapot: {TicketStatus}";
    public bool ShowTakeOver => HasForward(TicketForwardKey.TakeOver);
    public bool ShowStart => HasForward(TicketForwardKey.Start);
    public bool ShowResume => HasForward(TicketForwardKey.Resume);
    public bool CanFinishTicket => Forward(TicketForwardKey.Finish)?.IsEnabled == true;
    public string FinishTicketText => Forward(TicketForwardKey.Finish)?.Text ?? "Kész";

    private TicketForwardOption? Forward(TicketForwardKey key) =>
        IsTicket ? TicketWorkflow.ForwardOptions(TicketStatus, IsCompleted).FirstOrDefault(o => o.Key == key) : null;

    private bool HasForward(TicketForwardKey key) => Forward(key) is not null;

    // „…” menü (nem kész ügyön): más állapotba tétel; az aktuális állapot menüpontja tiltott.
    public bool HasTicketMenu => IsTicket && TicketWorkflow.HasMenu(TicketStatus, IsCompleted);
    public bool CanSetQuestionable => CanSetStatus(TicketStatuses.Questionable);
    public bool CanSetSos => CanSetStatus(TicketStatuses.Sos);
    public bool CanSetNew => CanSetStatus(TicketStatuses.New);

    private bool CanSetStatus(string target) => IsTicket && TicketWorkflow.IsMenuOptionEnabled(target, TicketStatus, IsCompleted);

    public const string ListHint = "A Beérkező ügyek listában néhány percen belül frissül.";

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

    /// <summary>Az ügyterv vagy a link-sablon megváltozott.</summary>
    public void RefreshTicket()
    {
        OnPropertyChanged(nameof(Ticket));
        OnPropertyChanged(nameof(IsTicket));
        OnPropertyChanged(nameof(TicketToolTip));
        OnPropertyChanged(nameof(HasTicketLink));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(TicketStatus));
        OnPropertyChanged(nameof(HasTicketStatus));
        OnPropertyChanged(nameof(NextTicketAction));
        foreach (var name in new[]
                 {
                     nameof(TicketNumberText), nameof(HasTicketNumber), nameof(ShowPlainCheck), nameof(ShowTicketCheck),
                     nameof(TicketCheckEnabled), nameof(ShowTakeOver), nameof(ShowStart), nameof(ShowResume),
                     nameof(CanFinishTicket), nameof(FinishTicketText), nameof(TicketMenuHeader), nameof(ShowPlanTitle),
                 })
        {
            OnPropertyChanged(name);
        }
        OnPropertyChanged(nameof(CanToggleComplete));
        OnPropertyChanged(nameof(CheckEnabled));
        OnPropertyChanged(nameof(CheckToolTip));
        OnPropertyChanged(nameof(HasTicketMenu));
        OnPropertyChanged(nameof(ShowNewReply));
        OnPropertyChanged(nameof(CanSetQuestionable));
        OnPropertyChanged(nameof(CanSetSos));
        OnPropertyChanged(nameof(CanSetNew));
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
    private Task ChangeAssignee() => _actions.ExplainTicketAssigneeAsync(this);

    /// <summary>A karika menüjének „Kész” pontja (csak In progress állapotban engedélyezett).</summary>
    [RelayCommand]
    private Task CompleteTicket() => _actions.ToggleCompleteAsync(this);

    [RelayCommand]
    private Task SetTicketStatus(string status) => _actions.SetTicketStatusAsync(this, status);

    [RelayCommand]
    private Task Edit() => _actions.EditAsync(this);

    [RelayCommand]
    private Task ToggleDetails() => _actions.ToggleDetailsAsync(this);

    [RelayCommand]
    private void Open() => _actions.OpenInPlanner(this);

    [RelayCommand]
    private void OpenTicket() => _actions.OpenTicket(this);

    /// <summary>A lista elemének neve képernyőolvasónak (a ListViewItem ezt olvassa fel).</summary>
    public override string ToString() => DisplayTitle;

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

    // Szöveg szerkesztése (a Planner legfeljebb ChecklistItem.MaxTitleLength karaktert enged)
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditLength), nameof(CounterText), nameof(CounterToolTip))]
    public partial string EditText { get; set; } = "";

    public int MaxLength => ChecklistItem.MaxTitleLength;
    public int EditLength => EditText?.Length ?? 0;
    public string CounterText => $"{EditLength}/{MaxLength}";

    public string CounterToolTip => EditLength >= MaxLength
        ? $"Elérted a {MaxLength} karakteres korlátot."
        : $"{EditLength} karakter (legfeljebb {MaxLength})";

    public string EditButtonName => $"Szöveg szerkesztése: {Title}";

    [RelayCommand]
    private void StartEdit()
    {
        foreach (var other in _owner.Checklist.Where(c => c != this && c.IsEditing))
        {
            other.IsEditing = false;
        }

        EditText = Title;
        IsEditing = true;
    }

    [RelayCommand]
    private Task SaveEdit() => _actions.RenameChecklistItemAsync(_owner, this, EditText);

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

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
