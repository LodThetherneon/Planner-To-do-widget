using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;
using Windows.Storage.Pickers;

namespace PlannerWidget.App.Services;

/// <summary>
/// ContentDialog-alapú párbeszédek egy adott ablakhoz. Egyszerre csak egy lehet nyitva (WinUI-korlát),
/// ezért a hívásokat sorba állítja. Megjelenítés előtt az ablakot láthatóvá/lenyitottá teszi.
/// </summary>
public sealed class DialogService(Func<XamlRoot?> xamlRoot, Func<nint> windowHandle, Func<ElementTheme> theme, Func<Task>? ensureVisible = null)
    : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, bool destructive = false)
    {
        var dialog = Create(title, Text(message));
        dialog.PrimaryButtonText = primaryText;
        dialog.CloseButtonText = "Mégse";
        dialog.DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary;
        if (destructive)
        {
            dialog.PrimaryButtonStyle = DangerButtonStyle();
        }

        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<DialogChoice> ChooseAsync(string title, string message, string primaryText, string? secondaryText, string closeText = "Mégse")
    {
        var dialog = Create(title, Text(message));
        dialog.PrimaryButtonText = primaryText;
        dialog.SecondaryButtonText = secondaryText ?? "";
        dialog.CloseButtonText = closeText;
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await ShowAsync(dialog) switch
        {
            ContentDialogResult.Primary => DialogChoice.Primary,
            ContentDialogResult.Secondary => DialogChoice.Secondary,
            _ => DialogChoice.None,
        };
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = Create(title, Text(message));
        dialog.CloseButtonText = "OK";
        dialog.DefaultButton = ContentDialogButton.Close;
        await ShowAsync(dialog);
    }

    public async Task<TaskEditModel?> EditTaskAsync(TaskEditModel current)
    {
        var title = new TextBox { Header = "Cím", Text = current.Title, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
        var due = new CalendarDatePicker
        {
            Header = "Határidő",
            PlaceholderText = "Nincs határidő",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsTodayHighlighted = true,
            FirstDayOfWeek = Windows.Globalization.DayOfWeek.Monday,
            Date = current.DueDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null,
        };
        var clear = new Button { Content = "Határidő törlése", Margin = new Thickness(0, 4, 0, 0) };
        clear.Click += (_, _) => due.Date = null;

        var priority = new ComboBox { Header = "Prioritás", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var p in Enum.GetValues<TaskPriority>())
        {
            priority.Items.Add(p.ToDisplayName());
        }

        priority.SelectedIndex = (int)current.Priority;

        var panel = new StackPanel { Spacing = 12, MinWidth = 280 };
        panel.Children.Add(title);
        var dueRow = new StackPanel { Spacing = 0 };
        dueRow.Children.Add(due);
        dueRow.Children.Add(clear);
        panel.Children.Add(dueRow);
        panel.Children.Add(priority);

        var dialog = Create("Feladat szerkesztése", panel);
        dialog.PrimaryButtonText = "Mentés";
        dialog.CloseButtonText = "Mégse";
        dialog.DefaultButton = ContentDialogButton.Primary;
        title.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(title.Text);
        title.Loaded += (_, _) => title.Focus(FocusState.Programmatic);

        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        return new TaskEditModel(
            title.Text.Trim(),
            due.Date is { } date ? DateOnly.FromDateTime(date.Date) : null,
            (TaskPriority)Math.Max(0, priority.SelectedIndex));
    }

    public async Task<string?> PickFieldAsync(string title, string instruction, IReadOnlyList<string> fieldNames, bool optional)
    {
        var search = new TextBox { PlaceholderText = "Keresés a mezőnevekben…" };
        var list = new ListView
        {
            Height = 260,
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false,
            ItemsSource = fieldNames.ToList(),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            CornerRadius = new CornerRadius(6),
        };
        if (fieldNames.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            var filtered = fieldNames.Where(n => n.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
            list.ItemsSource = filtered;
            if (filtered.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 300 };
        panel.Children.Add(Text(instruction));
        panel.Children.Add(search);
        panel.Children.Add(list);

        var dialog = Create(title, panel);
        dialog.PrimaryButtonText = "Kiválasztás";
        dialog.CloseButtonText = "Mégse";
        if (optional)
        {
            dialog.SecondaryButtonText = "Kihagyás";
        }

        dialog.DefaultButton = ContentDialogButton.Primary;
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is not null)
            {
                dialog.Tag = list.SelectedItem;
                dialog.Hide();
            }
        };
        search.Loaded += (_, _) => search.Focus(FocusState.Programmatic);

        var result = await ShowAsync(dialog);
        if (dialog.Tag is string picked)
        {
            return picked;
        }

        return result == ContentDialogResult.Primary ? list.SelectedItem as string : null;
    }

    public async Task<string?> PickPdfAsync()
    {
        try
        {
            if (ensureVisible is not null)
            {
                await ensureVisible();
            }

            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                CommitButtonText = "Kiválasztás",
            };
            picker.FileTypeFilter.Add(".pdf");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle());
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            Log.Error("Fájlválasztó hiba", ex);
            await ShowMessageAsync("Hiba", "A fájlválasztó nem nyitható meg: " + ex.Message);
            return null;
        }
    }

    public async Task<string?> PickFolderAsync()
    {
        try
        {
            if (ensureVisible is not null)
            {
                await ensureVisible();
            }

            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                CommitButtonText = "Mappa kiválasztása",
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle());
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            Log.Error("Mappaválasztó hiba", ex);
            await ShowMessageAsync("Hiba", "A mappaválasztó nem nyitható meg: " + ex.Message);
            return null;
        }
    }

    private ContentDialog Create(string title, object content) => new()
    {
        Title = title,
        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 0),
        },
        RequestedTheme = theme(),
    };

    private static TextBlock Text(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    private static Style DangerButtonStyle()
    {
        var style = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["AccentButtonStyle"] };
        style.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Brush(Ui.Red)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Ui.Brush(Ui.Red)));
        return style;
    }

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync();
        try
        {
            if (ensureVisible is not null)
            {
                await ensureVisible();
            }

            dialog.XamlRoot = xamlRoot() ?? throw new InvalidOperationException("Az ablak még nem jelent meg.");
            return await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
