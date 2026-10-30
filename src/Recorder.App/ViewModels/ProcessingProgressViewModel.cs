using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Services;

namespace Recorder.App.ViewModels;

public partial class ProcessingProgressViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ProcessingProgressViewModel(AppServices services)
    {
        _services = services;
        _services.Workflow.Changed += (_, _) => App.Current.Dispatcher.Invoke(Refresh);
        Refresh();
    }

    [ObservableProperty]
    private string _title = "Finalizing recording";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _canCancel;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    private string _errorDetail = string.Empty;

    [RelayCommand]
    private async Task CancelAsync()
    {
        await _services.Workflow.CancelCurrentOperationAsync(CancellationToken.None);
        Refresh();
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        await _services.Workflow.RetryCurrentOperationAsync(CancellationToken.None);
        Refresh();
    }

    public void Refresh()
    {
        var status = _services.Workflow.Status;
        StatusMessage = status.Message;
        Progress = status.ProgressFraction * 100;
        CanCancel = status.CanCancel;
        CanRetry = status.CanRetry;
        ErrorDetail = status.ErrorDetail ?? string.Empty;
        Title = status.Message.Contains("Transcrib", StringComparison.OrdinalIgnoreCase)
            ? "Transcription progress"
            : "Finalizing recording";
    }
}
