using CommunityToolkit.Mvvm.ComponentModel;

namespace Recorder.App.ViewModels;

public sealed partial class ExportSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _studyCode;

    [ObservableProperty]
    private bool _includeReview;

    [ObservableProperty]
    private string _reviewerInitials = string.Empty;

    [ObservableProperty]
    private bool _useReExportFolder;
}
