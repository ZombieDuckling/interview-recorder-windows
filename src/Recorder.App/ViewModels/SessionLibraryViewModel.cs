using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Services;
using Recorder.Core.Formats;

using System.Globalization;

namespace Recorder.App.ViewModels;

public partial class SessionRowViewModel : ObservableObject
{
    public SessionRowViewModel(SessionListItem item)
    {
        SessionCode = item.SessionCode;
        Directory = item.SessionDirectory;
        State = item.State;
        RecordedAt = item.RecordedAt;
        Duration = item.Duration;
    }

    public string SessionCode { get; }

    public string Directory { get; }

    public SessionState State { get; }

    public DateTimeOffset RecordedAt { get; }

    public TimeSpan? Duration { get; }

    public string DateDisplay => RecordedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public string DurationDisplay => Duration is null ? "—" : Duration.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}

public partial class SessionLibraryViewModel : ObservableObject
{
    private readonly AppServices _services;

    public SessionLibraryViewModel(AppServices services)
    {
        _services = services;
        Refresh();
    }

    public IList<SessionRowViewModel> Sessions { get; } = new List<SessionRowViewModel>();

    [ObservableProperty]
    private SessionRowViewModel? _selectedSession;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event EventHandler<SessionRowViewModel>? OpenRequested;

    [RelayCommand]
    private void Refresh()
    {
        Sessions.Clear();
        foreach (var item in _services.Workflow.ListSessions(_services.Settings.Current.DataRoot))
        {
            Sessions.Add(new SessionRowViewModel(item));
        }

        StatusMessage = $"{Sessions.Count} session(s) in library.";
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedSession is not null)
        {
            OpenRequested?.Invoke(this, SelectedSession);
        }
    }

    [RelayCommand]
    private async Task ReExportAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        try
        {
            await _services.Workflow.ExportAsync(SelectedSession.Directory, CancellationToken.None);
            StatusMessage = "Export requested.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ShowFolder()
    {
        if (SelectedSession is not { Directory: var dir } || !Directory.Exists(dir))
        {
            StatusMessage = "Session folder not found.";
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Delete session {SelectedSession.SessionCode} and all its files? This cannot be undone.",
            "Confirm delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _services.Retention.DeleteSessionAsync(SelectedSession.Directory, CancellationToken.None);
            StatusMessage = "Session deleted.";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
