using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Sync;

namespace PlannerWidget.App.ViewModels;

/// <summary>
/// Szinkronhibák (a „Szinkronhibák” To Do lista) – e-mail helyett a widgetben: kis jelzés a fejléc gombsorában,
/// rákattintva lenyíló lista „Megnyitás” / „Megoldva” gombokkal, plusz az életjel-figyelés. A Planner-frissítéssel
/// együtt frissül; hálózati hibánál csak naplóz (nem dob fel ablakot).
/// </summary>
public sealed partial class MainViewModel
{
    private bool _syncLoading;

    public ObservableCollection<SyncErrorGroupViewModel> SyncErrorGroups { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncIssues), nameof(SyncIsError), nameof(SyncBadgeText), nameof(SyncToolTip), nameof(HasNoSyncErrors))]
    public partial int SyncErrorCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncIssues), nameof(SyncBadgeText), nameof(SyncToolTip), nameof(HasHeartbeatWarning))]
    public partial string? HeartbeatWarning { get; set; }

    /// <summary>A jelzés csak akkor látszik, ha van megoldatlan hiba, vagy leállt az életjel.</summary>
    public bool HasSyncIssues => IsSignedIn && (SyncErrorCount > 0 || HeartbeatWarning is not null);

    public bool SyncIsError => SyncErrorCount > 0;
    public bool HasNoSyncErrors => SyncErrorCount == 0;
    public bool HasHeartbeatWarning => HeartbeatWarning is not null;
    public string SyncBadgeText => SyncErrorCount > 0 ? SyncErrorCount.ToString(CultureInfo.InvariantCulture) : "!";

    public string SyncToolTip => SyncErrorCount > 0
        ? $"Szinkronhibák: {SyncErrorCount}" + (HeartbeatWarning is not null ? " · a szinkron nem fut" : "") + " – kattints a részletekért"
        : HeartbeatWarning ?? "";

    /// <summary>A Planner-frissítés végén hívódik. Minden hibát elnyel (naplóz), a felületen nem jelez hibát.</summary>
    private async Task RefreshSyncErrorsAsync()
    {
        if (_syncLoading || !IsSignedIn)
        {
            return;
        }

        _syncLoading = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ApplySyncStatus(await _services.SyncErrors.LoadAsync(cts.Token));
        }
        catch (Exception ex)
        {
            Log.Warn($"A szinkronhibák nem tölthetők be: {ex.Message}");
        }
        finally
        {
            _syncLoading = false;
        }
    }

    private void ApplySyncStatus(SyncStatus status)
    {
        SyncErrorGroups.Clear();
        foreach (var group in status.Groups)
        {
            SyncErrorGroups.Add(new SyncErrorGroupViewModel(group, this));
        }

        SyncErrorCount = status.ErrorCount;
        HeartbeatWarning = status.Heartbeat.Message;
    }

    private void ClearSyncErrors()
    {
        SyncErrorGroups.Clear();
        SyncErrorCount = 0;
        HeartbeatWarning = null;
    }

    public void OpenSyncError(SyncErrorGroupViewModel group)
    {
        if (group.Group.Link is { } uri)
        {
            OpenUri(uri);
        }
        else
        {
            ShowStatus("Ehhez a hibához nincs link.", StatusKind.Warning);
        }
    }

    /// <summary>„Megoldva”: a csoport összes To Do bejegyzése status=completed.</summary>
    public async Task ResolveSyncErrorAsync(SyncErrorGroupViewModel group)
    {
        if (group.IsBusy)
        {
            return;
        }

        group.IsBusy = true;
        try
        {
            await _services.SyncErrors.ResolveAsync(group.Group.Entries);
            SyncErrorGroups.Remove(group);
            SyncErrorCount = Math.Max(0, SyncErrorCount - group.Group.Count);
            Log.Info($"Szinkronhiba megoldva: {group.Group.Title} ({group.Group.Count} db)");
            ShowStatus("Megoldva ✓", StatusKind.Success, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Log.Warn($"A szinkronhiba nem zárható le: {ex.Message}");
            ShowStatus("Nem sikerült lezárni – próbáld újra később", StatusKind.Warning);
        }
        finally
        {
            group.IsBusy = false;
        }
    }
}

/// <summary>Egy hibacsoport a lenyíló listában.</summary>
public sealed partial class SyncErrorGroupViewModel(SyncErrorGroup group, MainViewModel owner) : ObservableObject
{
    public SyncErrorGroup Group { get; } = group;
    public string ProcessName => Group.ProcessName;
    public bool HasLink => Group.Link is not null;

    /// <summary>„2× · utolsó: 10.07. 14:32” (helyi idő).</summary>
    public string DetailText
    {
        get
        {
            var when = Group.Latest is { } t ? t.ToLocalTime().ToString("MM.dd. HH:mm", CultureInfo.InvariantCulture) : "ismeretlen idő";
            return Group.Count > 1 ? $"{Group.Count}× · utolsó: {when}" : when;
        }
    }

    public string ResolveToolTip => Group.Count > 1
        ? $"Mind a {Group.Count} bejegyzést lezárja a „Szinkronhibák” listában"
        : "Lezárja a bejegyzést a „Szinkronhibák” listában";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private void Open() => owner.OpenSyncError(this);

    [RelayCommand]
    private Task Resolve() => owner.ResolveSyncErrorAsync(this);
}
