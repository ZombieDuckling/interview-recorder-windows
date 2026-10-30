using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Helpers;
using Recorder.App.Services;
using Recorder.Core.Abstractions;

namespace Recorder.App.ViewModels;

public partial class NewInterviewViewModel : ObservableObject
{
    private readonly AppServices _services;

    public NewInterviewViewModel(AppServices services)
    {
        _services = services;
        RefreshDevices();
    }

    public IList<DeviceRowViewModel> RenderDevices { get; } = new List<DeviceRowViewModel>();

    public IList<DeviceRowViewModel> CaptureDevices { get; } = new List<DeviceRowViewModel>();

    [ObservableProperty]
    private string _participantId = string.Empty;

    [ObservableProperty]
    private string _studyCode = string.Empty;

    [ObservableProperty]
    private DeviceRowViewModel? _selectedMeetingDevice;

    [ObservableProperty]
    private DeviceRowViewModel? _selectedMicrophone;

    [ObservableProperty]
    private bool _consentConfirmed;

    [ObservableProperty]
    private string _participantIdError = string.Empty;

    [ObservableProperty]
    private string _scopeNotice =
        "Meeting audio uses system loopback on the output device you select. It can include other sounds on this PC. Use headphones and a dedicated meeting output device when you can.";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool CanStart => ConsentConfirmed && string.IsNullOrEmpty(ParticipantIdError) && !string.IsNullOrWhiteSpace(ParticipantId);

    partial void OnParticipantIdChanged(string value)
    {
        ParticipantIdError = ParticipantIdValidator.TryValidate(value, out var reason) ? string.Empty : reason;
        OnPropertyChanged(nameof(CanStart));
    }

    partial void OnConsentConfirmedChanged(bool value) => OnPropertyChanged(nameof(CanStart));

    [RelayCommand]
    private void RefreshDevices()
    {
        RenderDevices.Clear();
        CaptureDevices.Clear();
        foreach (var d in _services.AudioCapture.ListDevices(AudioDeviceKind.Render))
        {
            RenderDevices.Add(new DeviceRowViewModel(d));
        }

        foreach (var d in _services.AudioCapture.ListDevices(AudioDeviceKind.Capture))
        {
            CaptureDevices.Add(new DeviceRowViewModel(d));
        }

        SelectedMeetingDevice ??= RenderDevices.FirstOrDefault(d => d.IsDefault) ?? RenderDevices.FirstOrDefault();
        SelectedMicrophone ??= CaptureDevices.FirstOrDefault(d => d.IsDefault) ?? CaptureDevices.FirstOrDefault();
    }

    public SessionStartRequest BuildStartRequest()
    {
        if (!ParticipantIdValidator.TryValidate(ParticipantId, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        if (!ConsentConfirmed)
        {
            throw new InvalidOperationException("Consent must be confirmed before recording.");
        }

        return new SessionStartRequest(
            ParticipantId.Trim(),
            string.IsNullOrWhiteSpace(StudyCode) ? null : StudyCode.Trim(),
            SelectedMeetingDevice?.Id,
            SelectedMicrophone?.Id,
            ConsentConfirmed);
    }
}
