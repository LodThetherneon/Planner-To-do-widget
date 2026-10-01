using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PlannerWidget.App.Services;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;

namespace PlannerWidget.App.ViewModels;

public enum StatusKind
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record PlanOption(string? Id, string Title)
{
    public override string ToString() => Title;
}

public sealed record BucketOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// A widget fő nézetmodellje: bejelentkezés, feladatlista (szűrés, rendezés, lapozás),
/// műveletek optimista frissítéssel, új feladat, státuszüzenetek. A munkaidő-rész a
/// MainViewModel.Work.cs fájlban van.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, ITaskActions
{
    public const int CompletedPageSize = 12;

    private readonly AppServices _services;
    private readonly IDialogService _dialogs;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly Dictionary<string, TaskItemViewModel> _itemCache = new();
    private List<PlannerTask> _tasks = [];
    private CancellationTokenSource? _signInCts;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private bool _refreshing;
    private bool _plansLoaded;

    public MainViewModel(AppServices services, IDialogService dialogs, DispatcherQueue dispatcher)
    {
        _services = services;
        _dialogs = dialogs;
        _dispatcher = dispatcher;

        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => StatusMessage = null;

        _refreshTimer = dispatcher.CreateTimer();
        _refreshTimer.IsRepeating = true;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(userInitiated: false);

        _clockTimer = dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(20);
        _clockTimer.IsRepeating = true;
        _clockTimer.Tick += (_, _) => OnClockTick();

        PlanFilterOptions.Add(AllPlans);
        SelectedPlanFilter = AllPlans;
        NewTaskDue = DateTimeOffset.Now.Date;
    }

    private static PlanOption AllPlans { get; } = new(null, "Minden terv");

    // ---- Állapot ---------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    public partial bool IsSignedIn { get; set; }

    public bool IsSignedOut => !IsSignedIn;

    public bool IsDemo => _services.IsDemo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInButtonText))]
    public partial bool IsSigningIn { get; set; }

    public string SignInButtonText => IsSigningIn ? "Megszakítás" : "Bejelentkezés";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoActiveTasks))]
    public partial bool IsBusy { get; set; }

    /// <summary>„Tipp: Alt+W bárhonnan előhozza a widgetet.”</summary>
    [ObservableProperty]
    public partial string HotkeyHint { get; set; } = "";

    [ObservableProperty]
    public partial bool IsOffline { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UserInitials))]
    public partial string? UserDisplayName { get; set; }

    public string UserInitials => Initials(UserDisplayName);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastRefreshText))]
    public partial DateTimeOffset? LastRefresh { get; set; }

    public string LastRefreshText => LastRefresh is { } t
        ? (IsOffline ? "Offline · " : "Frissítve ") + t.ToLocalTime().ToString("HH:mm")
        : "Még nincs frissítve";

    // Fejléc
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderTitle))]
    public partial int ActiveCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverdue), nameof(OverdueText))]
    public partial int OverdueCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDueToday), nameof(DueTodayText))]
    public partial int DueTodayCount { get; set; }

    public string HeaderTitle => IsSignedIn ? $"{ActiveCount} feladat" : "Planner Widget";
    public bool HasOverdue => IsSignedIn && OverdueCount > 0;
    public string OverdueText => $"{OverdueCount} lejárt";
    public bool HasDueToday => IsSignedIn && DueTodayCount > 0;
    public string DueTodayText => $"{DueTodayCount} mára";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial StatusKind StatusKind { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    // Hibasáv (InfoBar)
    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string ErrorTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial StatusKind ErrorSeverity { get; set; } = StatusKind.Error;

    // Listák
    public ObservableCollection<TaskItemViewModel> ActiveTasks { get; } = [];
    public ObservableCollection<TaskItemViewModel> CompletedPageTasks { get; } = [];
    public ObservableCollection<PlanOption> PlanFilterOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoActiveTasks), nameof(EmptyText))]
    public partial int VisibleActiveCount { get; set; }

    public bool HasNoActiveTasks => IsSignedIn && VisibleActiveCount == 0 && !IsBusy;

    public string EmptyText => IsFiltering
        ? "Nincs a szűrésnek megfelelő aktív feladat."
        : "Nincs aktív feladatod. Szép munka! 🎉";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCompleted), nameof(CompletedHeader))]
    public partial int CompletedTotal { get; set; }

    public bool HasCompleted => CompletedTotal > 0;
    public string CompletedHeader => $"Kész feladatok ({CompletedTotal})";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText), nameof(CanGoPrevious), nameof(CanGoNext))]
    public partial int CompletedPage { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageText), nameof(CanGoPrevious), nameof(CanGoNext), nameof(HasMultiplePages))]
    public partial int CompletedPageCount { get; set; } = 1;

    public string PageText => $"{CompletedPage} / {CompletedPageCount}";
    public bool CanGoPrevious => CompletedPage > 1;
    public bool CanGoNext => CompletedPage < CompletedPageCount;
    public bool HasMultiplePages => CompletedPageCount > 1;

    [ObservableProperty]
    public partial bool IsCompletedExpanded { get; set; }

    // Szűrés
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial PlanOption? SelectedPlanFilter { get; set; }

    private bool IsFiltering => !string.IsNullOrWhiteSpace(SearchText) || SelectedPlanFilter?.Id is not null;

    partial void OnSearchTextChanged(string value)
    {
        RebuildLists();
        ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedPlanFilterChanged(PlanOption? value)
    {
        RebuildLists();
        ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Új lista (első betöltés, szűrésváltás): a nézet a lista elejére görget.</summary>
    public event EventHandler? ScrollToTopRequested;

    // ---- Indítás ---------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        ApplyRefreshInterval();
        _clockTimer.Start();
        RestoreWorkState();

        if (_services.Planner.LoadCachedSnapshot() is { } cached)
        {
            ApplySnapshot(cached);
            LastRefresh = cached.FetchedAt;
            IsSignedIn = true; // Ideiglenesen: a gyorsítótárból mutatjuk, amíg a csendes bejelentkezés lefut.
        }

        bool signedIn;
        try
        {
            IsBusy = true;
            signedIn = await _services.Auth.TrySignInSilentlyAsync();
        }
        catch (GraphNetworkException)
        {
            // Offline indulás: a gyorsítótárból mutatjuk az utolsó állapotot, és időnként újrapróbáljuk.
            IsBusy = false;
            IsOffline = true;
            ShowError("Nincs kapcsolat", "Nincs internetkapcsolat – a legutóbb letöltött állapot látható.", StatusKind.Warning);
            ScheduleOfflineRetry();
            return;
        }
        catch (Exception ex)
        {
            Log.Error("Csendes bejelentkezés hiba", ex);
            signedIn = false;
        }
        finally
        {
            IsBusy = false;
        }

        if (signedIn)
        {
            await OnSignedInAsync(playSound: true);
        }
        else
        {
            SetSignedOut(null);
        }
    }

    public void ApplyRefreshInterval()
    {
        _refreshTimer.Interval = TimeSpan.FromMinutes(_services.Settings.Current.RefreshIntervalMinutes);
        if (IsSignedIn)
        {
            _refreshTimer.Start();
        }
    }

    private void ScheduleOfflineRetry()
    {
        var retry = _dispatcher.CreateTimer();
        retry.Interval = TimeSpan.FromMinutes(1);
        retry.IsRepeating = false;
        retry.Tick += async (_, _) => await InitializeAsync();
        retry.Start();
    }

    /// <summary>Megjelenítéskor (gyorsbillentyű, tálca) frissít, ha az adat már régi.</summary>
    public void RefreshIfStale()
    {
        if (IsSignedIn && (LastRefresh is null || DateTimeOffset.Now - LastRefresh > TimeSpan.FromMinutes(2)))
        {
            _ = RefreshAsync(userInitiated: false);
        }
    }

    private void OnClockTick()
    {
        UpdateWorkElapsed();

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _today)
        {
            _today = today;
            foreach (var item in _itemCache.Values)
            {
                item.RefreshComputed();
            }

            NewTaskDue = DateTimeOffset.Now.Date;
            RebuildLists();
        }
    }

    // ---- Bejelentkezés ---------------------------------------------------------------------

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsSigningIn)
        {
            _signInCts?.Cancel();
            return;
        }

        IsSigningIn = true;
        _signInCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        ShowStatus("Bejelentkezés… (folytasd a megnyíló ablakban)", StatusKind.Info, TimeSpan.FromMinutes(5));
        try
        {
            await _services.Auth.SignInInteractiveAsync(_signInCts.Token);
            await OnSignedInAsync(playSound: true);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Bejelentkezés megszakítva.", StatusKind.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("Bejelentkezési hiba", ex);
            ShowError("Sikertelen bejelentkezés", ex.Message);
            StatusMessage = null;
        }
        finally
        {
            IsSigningIn = false;
            _signInCts?.Dispose();
            _signInCts = null;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (!await _dialogs.ConfirmAsync("Kijelentkezés", "Biztosan kijelentkezel? A feladatlista a következő bejelentkezésig nem frissül.", "Kijelentkezés"))
        {
            return;
        }

        await _services.Auth.SignOutAsync();
        _services.Planner.ResetSession();
        _services.Planner.ClearCache();
        SetSignedOut("Sikeres kijelentkezés.");
    }

    private async Task OnSignedInAsync(bool playSound)
    {
        IsSignedIn = true;
        IsOffline = false;
        CloseError();
        _refreshTimer.Start();
        OnPropertyChanged(nameof(HeaderTitle));
        await RefreshAsync(userInitiated: playSound);

        try
        {
            var me = await _services.Planner.GetMeAsync();
            UserDisplayName = string.IsNullOrWhiteSpace(me.DisplayName) ? _services.Auth.AccountName : me.DisplayName;
        }
        catch (Exception ex)
        {
            Log.Warn("A felhasználói profil nem kérhető le", ex);
            UserDisplayName ??= _services.Auth.AccountName;
        }
    }

    private void SetSignedOut(string? message)
    {
        IsSignedIn = false;
        IsOffline = false;
        _refreshTimer.Stop();
        _tasks = [];
        _itemCache.Clear();
        _plansLoaded = false;
        AddPlans.Clear();
        AddBuckets.Clear();
        UserDisplayName = null;
        LastRefresh = null;
        RebuildLists();
        CloseError();
        OnPropertyChanged(nameof(HeaderTitle));
        if (message is not null)
        {
            ShowStatus(message, StatusKind.Success);
        }
    }

    // ---- Frissítés -------------------------------------------------------------------------

    [RelayCommand]
    private Task Refresh() => RefreshAsync(userInitiated: true);

    public async Task RefreshAsync(bool userInitiated)
    {
        if (_refreshing || !IsSignedIn || IsSigningIn)
        {
            return;
        }

        // Folyamatban lévő művelet közben az automatikus frissítés felülírhatná az optimista állapotot.
        if (!userInitiated && _itemCache.Values.Any(i => i.IsBusy))
        {
            return;
        }

        _refreshing = true;
        IsBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var snapshot = await _services.Planner.LoadSnapshotAsync(cts.Token);
            ApplySnapshot(snapshot);
            LastRefresh = snapshot.FetchedAt;
            if (IsOffline || ErrorSeverity != StatusKind.Error)
            {
                CloseError();
            }

            IsOffline = false;
            OnPropertyChanged(nameof(LastRefreshText));
            if (userInitiated)
            {
                _services.Sounds.Play(SoundEffect.Start);
            }

            MaybeSendDailyNotification();
        }
        catch (NotSignedInException)
        {
            SetSignedOut(null);
            ShowError("Bejelentkezés szükséges", "A bejelentkezés lejárt. Jelentkezz be újra.", StatusKind.Warning);
        }
        catch (GraphNetworkException)
        {
            IsOffline = true;
            OnPropertyChanged(nameof(LastRefreshText));
            ShowError("Nincs kapcsolat", "Nincs internetkapcsolat – a legutóbb letöltött állapot látható.", StatusKind.Warning);
        }
        catch (OperationCanceledException)
        {
            ShowError("Időtúllépés", "A Planner nem válaszolt időben. Később automatikusan újrapróbálom.", StatusKind.Warning);
        }
        catch (GraphApiException ex)
        {
            ShowError("Planner hiba", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error("Frissítési hiba", ex);
            ShowError("Váratlan hiba", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _refreshing = false;
            OnPropertyChanged(nameof(HasNoActiveTasks));
        }
    }

    private void ApplySnapshot(PlannerSnapshot snapshot)
    {
        var first = _tasks.Count == 0;
        _tasks = snapshot.Tasks.ToList();
        UpdatePlanFilterOptions();
        RebuildLists();
        if (first)
        {
            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdatePlanFilterOptions()
    {
        var selectedId = SelectedPlanFilter?.Id;
        var plans = _tasks.Select(t => t.PlanId).Distinct()
            .Select(id => new PlanOption(id, GetPlanTitle(id) ?? "(ismeretlen terv)"))
            .OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var desired = new List<PlanOption> { AllPlans };
        desired.AddRange(plans);
        if (!PlanFilterOptions.SequenceEqual(desired))
        {
            PlanFilterOptions.Clear();
            foreach (var option in desired)
            {
                PlanFilterOptions.Add(option);
            }

            SelectedPlanFilter = PlanFilterOptions.FirstOrDefault(p => p.Id == selectedId) ?? AllPlans;
        }
    }

    private void RebuildLists()
    {
        var filtered = _tasks.Where(Matches).ToList();
        var active = TaskOrdering.SortActive(filtered, _today);
        var completed = TaskOrdering.SortCompleted(filtered);

        Sync(ActiveTasks, active.Select(GetItem).ToList());

        CompletedTotal = completed.Count;
        CompletedPageCount = Math.Max(1, (completed.Count + CompletedPageSize - 1) / CompletedPageSize);
        CompletedPage = Math.Clamp(CompletedPage, 1, CompletedPageCount);
        Sync(CompletedPageTasks, completed.Skip((CompletedPage - 1) * CompletedPageSize).Take(CompletedPageSize).Select(GetItem).ToList());

        var counts = TaskOrdering.Count(_tasks, _today);
        ActiveCount = counts.Active;
        OverdueCount = counts.Overdue;
        DueTodayCount = counts.DueToday;
        VisibleActiveCount = active.Count;
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HasOverdue));
        OnPropertyChanged(nameof(HasDueToday));
        OnPropertyChanged(nameof(HasNoActiveTasks));
        OnPropertyChanged(nameof(EmptyText));

        // Törölt feladatok nézetmodelljeinek eldobása.
        var ids = _tasks.Select(t => t.Id).ToHashSet();
        foreach (var stale in _itemCache.Keys.Where(k => !ids.Contains(k)).ToList())
        {
            _itemCache.Remove(stale);
        }

        TrayTooltipChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Matches(PlannerTask task)
    {
        if (SelectedPlanFilter?.Id is { } planId && task.PlanId != planId)
        {
            return false;
        }

        var query = SearchText?.Trim();
        return string.IsNullOrEmpty(query)
               || task.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || (GetPlanTitle(task.PlanId)?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private TaskItemViewModel GetItem(PlannerTask task)
    {
        if (_itemCache.TryGetValue(task.Id, out var existing))
        {
            existing.Update(task);
            return existing;
        }

        var created = new TaskItemViewModel(task, this);
        _itemCache[task.Id] = created;
        return created;
    }

    /// <summary>Az ObservableCollection minimális módosítása (mozgatás/beszúrás/törlés), hogy a lista ne villogjon.</summary>
    private static void Sync<T>(ObservableCollection<T> target, IList<T> desired) where T : class
    {
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], desired[i]))
            {
                continue;
            }

            var existing = target.IndexOf(desired[i]);
            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanGoPrevious)
        {
            CompletedPage--;
            RebuildLists();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanGoNext)
        {
            CompletedPage++;
            RebuildLists();
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        SelectedPlanFilter = AllPlans;
    }

    // ---- Feladat-műveletek (ITaskActions) --------------------------------------------------

    public string? GetPlanTitle(string planId) =>
        _services.Planner.PlanTitles.TryGetValue(planId, out var title) ? title : null;

    public async Task ToggleCompleteAsync(TaskItemViewModel item)
    {
        if (item.IsBusy)
        {
            return;
        }

        var original = item.Model;
        var completing = !original.IsCompleted;
        var optimistic = original with
        {
            PercentComplete = completing ? 100 : 0,
            CompletedDateTime = completing ? DateTimeOffset.Now : null,
        };

        ReplaceTask(optimistic);
        _services.Sounds.Play(completing ? SoundEffect.Complete : SoundEffect.Reopen);
        item.IsBusy = true;
        try
        {
            var updated = await _services.Planner.Client.UpdateTaskAsync(original, new TaskPatch { PercentComplete = completing ? 100 : 0 });
            ReplaceTask(updated);
            ShowStatus(completing ? "Kész ✓" : "Visszanyitva", StatusKind.Success, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            ReplaceTask(original);
            HandleActionError("A feladat állapota nem módosítható", ex);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task DeleteAsync(TaskItemViewModel item)
    {
        if (item.IsBusy)
        {
            return;
        }

        if (_services.Settings.Current.ConfirmDelete &&
            !await _dialogs.ConfirmAsync("Feladat törlése", $"Biztosan törlöd?\n\n„{item.Title}”\n\nA törlés nem vonható vissza.", "Törlés", destructive: true))
        {
            return;
        }

        var original = item.Model;
        _tasks.RemoveAll(t => t.Id == original.Id);
        RebuildLists();
        item.IsBusy = true;
        try
        {
            await _services.Planner.Client.DeleteTaskAsync(original);
            ShowStatus("Feladat törölve.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            _tasks.Add(original);
            RebuildLists();
            HandleActionError("A feladat nem törölhető", ex);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task EditAsync(TaskItemViewModel item)
    {
        var original = item.Model;
        var edited = await _dialogs.EditTaskAsync(new TaskEditModel(original.Title, original.DueDate, original.PriorityLevel));
        if (edited is null)
        {
            return;
        }

        var patch = new TaskPatch
        {
            Title = edited.Title != original.Title ? edited.Title : null,
            ChangeDueDate = edited.DueDate != original.DueDate,
            DueDate = edited.DueDate,
            Priority = edited.Priority != original.PriorityLevel ? edited.Priority : null,
        };
        if (patch.IsEmpty)
        {
            return;
        }

        ReplaceTask(original with
        {
            Title = edited.Title,
            DueDate = edited.DueDate,
            Priority = edited.Priority != original.PriorityLevel ? edited.Priority.ToPlanner() : original.Priority,
        });
        item.IsBusy = true;
        try
        {
            ReplaceTask(await _services.Planner.Client.UpdateTaskAsync(original, patch));
            ShowStatus("Mentve.", StatusKind.Success, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            ReplaceTask(original);
            HandleActionError("A módosítás nem menthető", ex);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task ToggleDetailsAsync(TaskItemViewModel item)
    {
        item.IsExpanded = !item.IsExpanded;
        if (!item.IsExpanded || item.IsLoadingDetails)
        {
            return;
        }

        if (!item.HasDescription && !item.HasChecklist && item.Details is null)
        {
            return; // Nincs mit betölteni.
        }

        item.IsLoadingDetails = true;
        try
        {
            item.ApplyDetails(await _services.Planner.Client.GetTaskDetailsAsync(item.Id));
            ReplaceTask(item.Model);
        }
        catch (Exception ex)
        {
            HandleActionError("A részletek nem tölthetők be", ex);
        }
        finally
        {
            item.IsLoadingDetails = false;
        }
    }

    public async Task SetChecklistItemAsync(TaskItemViewModel item, ChecklistItemViewModel checklistItem)
    {
        if (item.Details is null)
        {
            return;
        }

        var desired = checklistItem.IsChecked;
        checklistItem.IsBusy = true;
        try
        {
            var details = await _services.Planner.Client.SetChecklistItemCheckedAsync(item.Details, checklistItem.Id, desired);
            item.ApplyDetails(details);
            ReplaceTask(item.Model);
        }
        catch (Exception ex)
        {
            checklistItem.Revert(!desired);
            HandleActionError("A checklist elem nem módosítható", ex);
        }
        finally
        {
            checklistItem.IsBusy = false;
        }
    }

    public void OpenInPlanner(TaskItemViewModel item) => OpenUri(GraphConfig.TaskWebUri(item.Id));

    [RelayCommand]
    private void OpenPlanner() => OpenUri(GraphConfig.PlannerHomeUri);

    private static void OpenUri(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"A böngésző nem nyitható meg: {uri}", ex);
        }
    }

    private void ReplaceTask(PlannerTask task)
    {
        var index = _tasks.FindIndex(t => t.Id == task.Id);
        if (index >= 0)
        {
            _tasks[index] = task;
        }
        else
        {
            _tasks.Add(task);
        }

        RebuildLists();
        _services.Planner.SaveSnapshot(new PlannerSnapshot(_tasks.ToList(), _services.Planner.PlanTitles, LastRefresh ?? DateTimeOffset.Now));
    }

    private void HandleActionError(string title, Exception ex)
    {
        switch (ex)
        {
            case NotSignedInException:
                SetSignedOut(null);
                ShowError("Bejelentkezés szükséges", "A bejelentkezés lejárt. Jelentkezz be újra.", StatusKind.Warning);
                break;
            case GraphNetworkException:
                ShowError(title, "Nincs internetkapcsolat. Próbáld újra később.", StatusKind.Warning);
                break;
            case GraphApiException api:
                ShowError(title, api.Message);
                if (api.IsNotFound || api.IsConflict)
                {
                    _ = RefreshAsync(userInitiated: false);
                }

                break;
            default:
                Log.Error(title, ex);
                ShowError(title, ex.Message);
                break;
        }
    }

    // ---- Új feladat ------------------------------------------------------------------------

    public ObservableCollection<PlanOption> AddPlans { get; } = [];
    public ObservableCollection<BucketOption> AddBuckets { get; } = [];

    public static IReadOnlyList<string> PriorityOptions { get; } =
        [TaskPriority.Urgent.ToDisplayName(), TaskPriority.Important.ToDisplayName(), TaskPriority.Medium.ToDisplayName(), TaskPriority.Low.ToDisplayName()];

    [ObservableProperty]
    public partial bool IsAddPanelOpen { get; set; }

    [ObservableProperty]
    public partial PlanOption? NewTaskPlan { get; set; }

    [ObservableProperty]
    public partial BucketOption? NewTaskBucket { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial string NewTaskTitle { get; set; } = "";

    [ObservableProperty]
    public partial DateTimeOffset? NewTaskDue { get; set; }

    [ObservableProperty]
    public partial int NewTaskPriorityIndex { get; set; } = 2;

    [ObservableProperty]
    public partial bool IsLoadingBuckets { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTaskCommand))]
    public partial bool IsAdding { get; set; }

    /// <summary>A nézet erre figyelve teszi a fókuszt a címmezőbe.</summary>
    public event EventHandler? AddPanelOpened;

    public event EventHandler? TrayTooltipChanged;

    [RelayCommand]
    private async Task ToggleAddPanelAsync()
    {
        IsAddPanelOpen = !IsAddPanelOpen;
        if (!IsAddPanelOpen)
        {
            return;
        }

        AddPanelOpened?.Invoke(this, EventArgs.Empty);
        if (!_plansLoaded)
        {
            await LoadAddPlansAsync();
        }
    }

    public async Task OpenAddPanelAsync()
    {
        if (!IsAddPanelOpen)
        {
            await ToggleAddPanelAsync();
        }
        else
        {
            AddPanelOpened?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task LoadAddPlansAsync()
    {
        try
        {
            var plans = await _services.Planner.GetPlansAsync(_tasks);
            AddPlans.Clear();
            foreach (var plan in plans)
            {
                AddPlans.Add(new PlanOption(plan.Id, plan.Title));
            }

            _plansLoaded = true;
            var lastPlan = _services.Settings.Current.LastPlanId;
            NewTaskPlan = AddPlans.FirstOrDefault(p => p.Id == lastPlan) ?? AddPlans.FirstOrDefault();
        }
        catch (Exception ex)
        {
            HandleActionError("A tervek nem tölthetők be", ex);
        }
    }

    async partial void OnNewTaskPlanChanged(PlanOption? value)
    {
        AddBuckets.Clear();
        NewTaskBucket = null;
        if (value?.Id is not { } planId)
        {
            return;
        }

        IsLoadingBuckets = true;
        try
        {
            var buckets = await _services.Planner.GetBucketsAsync(planId);
            if (NewTaskPlan?.Id != planId)
            {
                return; // Közben másik tervet választottak.
            }

            foreach (var bucket in buckets)
            {
                AddBuckets.Add(new BucketOption(bucket.Id, string.IsNullOrWhiteSpace(bucket.Name) ? "(névtelen)" : bucket.Name));
            }

            var saved = _services.Settings.Current.DefaultBucketByPlan.GetValueOrDefault(planId);
            _suppressBucketSave = true;
            NewTaskBucket = AddBuckets.FirstOrDefault(b => b.Id == saved) ?? AddBuckets.FirstOrDefault();
            _suppressBucketSave = false;
        }
        catch (Exception ex)
        {
            HandleActionError("Az oszlopok (bucketek) nem tölthetők be", ex);
        }
        finally
        {
            IsLoadingBuckets = false;
        }
    }

    private bool _suppressBucketSave;

    partial void OnNewTaskBucketChanged(BucketOption? value)
    {
        if (_suppressBucketSave || value is null || NewTaskPlan?.Id is not { } planId)
        {
            return;
        }

        _services.Settings.Update(s => s.DefaultBucketByPlan[planId] = value.Id);
    }

    private bool CanAddTask() => !IsAdding && !string.IsNullOrWhiteSpace(NewTaskTitle);

    /// <summary>Határidő nélküli új feladat (a dátumválasztó magától nem üríthető).</summary>
    [RelayCommand]
    private void ClearNewTaskDue() => NewTaskDue = null;

    [RelayCommand(CanExecute = nameof(CanAddTask))]
    private async Task AddTaskAsync()
    {
        var title = NewTaskTitle.Trim();
        if (NewTaskPlan?.Id is not { } planId)
        {
            ShowStatus("Válassz tervet.", StatusKind.Warning);
            return;
        }

        if (NewTaskBucket is not { } bucket)
        {
            ShowStatus("Válassz oszlopot (bucketet).", StatusKind.Warning);
            return;
        }

        IsAdding = true;
        try
        {
            var me = await _services.Planner.GetMeAsync();
            var priority = (TaskPriority)Math.Clamp(NewTaskPriorityIndex, 0, 3);
            DateOnly? due = NewTaskDue is { } d ? DateOnly.FromDateTime(d.Date) : null;
            var created = await _services.Planner.Client.CreateTaskAsync(new NewTaskRequest(planId, bucket.Id, title, me.Id, due, priority));

            _services.Settings.Update(s => s.LastPlanId = planId);
            ReplaceTask(created);
            NewTaskTitle = "";
            NewTaskDue = DateTimeOffset.Now.Date;
            NewTaskPriorityIndex = 2;
            IsAddPanelOpen = false;
            ShowStatus("Feladat létrehozva ✓", StatusKind.Success);
        }
        catch (Exception ex)
        {
            HandleActionError("A feladat nem hozható létre", ex);
        }
        finally
        {
            IsAdding = false;
        }
    }

    // ---- Státusz és hibák ------------------------------------------------------------------

    public void ShowStatus(string message, StatusKind kind, TimeSpan? duration = null)
    {
        StatusKind = kind;
        StatusMessage = message;
        _statusTimer.Stop();
        _statusTimer.Interval = duration ?? TimeSpan.FromSeconds(3);
        _statusTimer.Start();
    }

    public void ShowError(string title, string message, StatusKind severity = StatusKind.Error)
    {
        ErrorTitle = title;
        ErrorMessage = message;
        ErrorSeverity = severity;
        IsErrorOpen = true;
    }

    [RelayCommand]
    private void CloseError() => IsErrorOpen = false;

    private void MaybeSendDailyNotification()
    {
        var settings = _services.Settings.Current;
        var now = DateTime.Now;
        if (!settings.NotificationsEnabled || settings.LastDailyNotification == _today || now.Hour < 7)
        {
            return;
        }

        var urgent = TaskOrdering.SortActive(_tasks, _today)
            .Where(t => TaskOrdering.GetDueState(t, _today) is DueState.Overdue or DueState.DueToday)
            .ToList();
        _services.Settings.Update(s => s.LastDailyNotification = _today);
        if (urgent.Count == 0)
        {
            return;
        }

        var parts = new List<string>();
        if (DueTodayCount > 0) parts.Add($"{DueTodayCount} feladat ma esedékes");
        if (OverdueCount > 0) parts.Add($"{OverdueCount} lejárt");
        _services.Notifications.Show("Mai teendők", string.Join(", ", parts) + ".", urgent.Select(t => "• " + t.Title));
    }

    public string TrayTooltip => !IsSignedIn
        ? "Planner Widget – nincs bejelentkezve"
        : $"Planner Widget – {ActiveCount} feladat" + (OverdueCount > 0 ? $", {OverdueCount} lejárt" : "") +
          (IsWorkRunning ? $"\nMunkaidő: {WorkElapsedText}" : "");

    private static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}
