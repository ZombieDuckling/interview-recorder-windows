using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;
using Recorder.Core.IO;
using Recorder.Export;
using Recorder.Export.Transcript;
using TranscriptDocument = Recorder.Core.Formats.Transcript;

namespace Recorder.App.ViewModels;

public sealed partial class TranscriptSegmentViewModel : ObservableObject
{
    public TranscriptSegmentViewModel(TranscriptSegment segment)
    {
        Id = segment.Id;
        StartSeconds = segment.Start;
        Speaker = segment.Speaker;
        SpeakerVerified = segment.SpeakerVerified;
        Text = segment.Text;
    }

    public int Id { get; }

    public double StartSeconds { get; }

    public string StartDisplay => TimeSpan.FromSeconds(StartSeconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _speaker;

    [ObservableProperty]
    private bool _speakerVerified;

    [ObservableProperty]
    private string _text;

    public void ApplyTo(TranscriptSegment segment)
    {
        segment.Speaker = Speaker;
        segment.SpeakerVerified = SpeakerVerified;
        segment.Text = Text;
    }
}

public sealed partial class TranscriptEditorViewModel : ObservableObject
{
    private readonly string _transcriptPath;
    private readonly string _audioPath;
    private TranscriptDocument _document;

    public TranscriptEditorViewModel(string sessionFolder, TranscriptDocument document, string audioFileName = "interview_audio.mp4")
    {
        SessionFolder = sessionFolder;
        _transcriptPath = Path.Combine(sessionFolder, "transcript.json");
        _audioPath = Path.Combine(sessionFolder, audioFileName);
        _document = document;
        ExportSettings = new ExportSettingsViewModel();
        Segments = new ObservableCollection<TranscriptSegmentViewModel>(
            document.Segments.OrderBy(s => s.Start).Select(s => new TranscriptSegmentViewModel(s)));
    }

    public string SessionFolder { get; }

    public ObservableCollection<TranscriptSegmentViewModel> Segments { get; }

    [ObservableProperty]
    private TranscriptSegmentViewModel? _selectedSegment;

    [ObservableProperty]
    private bool _isPlaying;

    public string AudioPath => _audioPath;

    [RelayCommand]
    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void SelectPreviousSegment()
    {
        if (Segments.Count == 0)
        {
            return;
        }

        var index = SelectedSegment is null ? 0 : Segments.IndexOf(SelectedSegment);
        index = index <= 0 ? Segments.Count - 1 : index - 1;
        SelectedSegment = Segments[index];
    }

    [RelayCommand]
    private void SelectNextSegment()
    {
        if (Segments.Count == 0)
        {
            return;
        }

        var index = SelectedSegment is null ? 0 : Segments.IndexOf(SelectedSegment);
        index = index >= Segments.Count - 1 ? 0 : index + 1;
        SelectedSegment = Segments[index];
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        SyncSegmentsToDocument();
        _document.UpdatedAt = DateTimeOffset.UtcNow;
        await TranscriptIO.WriteAsync(_transcriptPath, _document, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportDocxAsync(CancellationToken cancellationToken)
    {
        SyncSegmentsToDocument();
        var exportSettings = ExportSettings ?? new ExportSettingsViewModel();
        var exportedAt = DateTimeOffset.UtcNow;
        var outputPath = exportSettings.UseReExportFolder
            ? ExportPaths.ReExportPath(SessionFolder, exportedAt.ToLocalTime())
            : Path.Combine(SessionFolder, ExportPaths.PrimaryDocxFileName);

        var duration = TimeSpan.FromSeconds(_document.Segments.Max(s => s.End));
        var request = new DocxExportRequest(
            SessionCode: _document.SessionCode,
            RecordedAt: _document.CreatedAt,
            Duration: duration,
            StudyCode: exportSettings.StudyCode,
            EngineVersion: _document.Engine.Version,
            ModelName: _document.Engine.Model,
            Segments: TranscriptMapping.ToDocxSegments(_document),
            OutputPath: outputPath,
            ExportedAt: exportedAt,
            ReviewerInitials: exportSettings.IncludeReview ? exportSettings.ReviewerInitials : null,
            ReviewedAt: exportSettings.IncludeReview ? exportedAt : null);

        var exporter = new DocxExporter();
        await exporter.ExportAsync(request, cancellationToken).ConfigureAwait(true);
    }

    public ExportSettingsViewModel? ExportSettings { get; set; }

    public void ApplySegmentEdits(int segmentId, string text, string speaker, bool speakerVerified)
    {
        var at = DateTimeOffset.UtcNow;
        TranscriptEditor.EditSegmentText(_document, segmentId, text, at);
        TranscriptEditor.RelabelSpeaker(_document, segmentId, speaker, speakerVerified, at);
        var vm = Segments.First(s => s.Id == segmentId);
        vm.Text = text;
        vm.Speaker = speaker;
        vm.SpeakerVerified = speakerVerified;
    }

    private void SyncSegmentsToDocument()
    {
        foreach (var vm in Segments)
        {
            var segment = _document.Segments.First(s => s.Id == vm.Id);
            if (segment.Text != vm.Text)
            {
                TranscriptEditor.EditSegmentText(_document, vm.Id, vm.Text, DateTimeOffset.UtcNow);
            }

            if (segment.Speaker != vm.Speaker || segment.SpeakerVerified != vm.SpeakerVerified)
            {
                TranscriptEditor.RelabelSpeaker(_document, vm.Id, vm.Speaker, vm.SpeakerVerified, DateTimeOffset.UtcNow);
            }

            vm.ApplyTo(segment);
        }
    }
}
