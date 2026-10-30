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
    private string _studyCodeError = string.Empty;

    [ObservableProperty]
    private string _scopeNotice =
        "Meeting audio uses system loopback on the output device you select. It can include other sounds on this PC. Use headphones and a dedicated meeting output device when you can.";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasMicrophone => SelectedMicrophone is not null;

    public bool HasMeetingDevice => SelectedMeetingDevice is not null;

    public bool CanStart =>
        ConsentConfirmed
        && string.IsNullOrEmpty(ParticipantIdError)
        && string.IsNullOrEmpty(StudyCodeError)
        && !string.IsNullOrWhiteSpace(ParticipantId)
        && (HasMicrophone || HasMeetingDevice);

    partial void OnParticipantIdChanged(string value)
    {
        ParticipantIdError = ParticipantIdValidator.TryValidate(value, out var reason) ? string.Empty : reason;
        NotifyCanStartChanged();
    }

    partial void OnStudyCodeChanged(string value)
    {
        StudyCodeError = StudyCodeValidator.TryValidateOptional(value, out var reason) ? string.Empty : reason;
        NotifyCanStartChanged();
    }

    partial void OnConsentConfirmedChanged(bool value) => NotifyCanStartChanged();

    partial void OnSelectedMicrophoneChanged(DeviceRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasMicrophone));
        NotifyCanStartChanged();
    }

    partial void OnSelectedMeetingDeviceChanged(DeviceRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasMeetingDevice));
        NotifyCanStartChanged();
    }

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

    public bool ConfirmScopeNotice()
    {
        return System.Windows.MessageBox.Show(
            ScopeNotice + Environment.NewLine + Environment.NewLine + "Continue and start recording?",
            "System audio scope",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
    }

    public bool ConfirmMissingInputs()
    {
        if (HasMicrophone && HasMeetingDevice)
        {
            return true;
        }

        if (!HasMicrophone && !HasMeetingDevice)
        {
            StatusMessage = "Select at least a microphone or a meeting output device.";
            return false;
        }

        var missing = !HasMicrophone ? "microphone" : "meeting output device";
        var only = HasMicrophone ? "microphone only" : "meeting audio only";
        return System.Windows.MessageBox.Show(
            $"No {missing} is selected. Recording will use {only}. Continue?",
            "Single audio input",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
    }

    public SessionStartRequest BuildStartRequest()
    {
        if (!ParticipantIdValidator.TryValidate(ParticipantId, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        if (!StudyCodeValidator.TryValidateOptional(StudyCode, out var studyReason))
        {
            throw new InvalidOperationException(studyReason);
        }

        if (!ConsentConfirmed)
        {
            throw new InvalidOperationException("Consent must be confirmed before recording.");
        }

        if (!HasMicrophone && !HasMeetingDevice)
        {
            throw new InvalidOperationException("Select at least a microphone or meeting output device.");
        }

        return new SessionStartRequest(
            ParticipantId.Trim(),
            string.IsNullOrWhiteSpace(StudyCode) ? null : StudyCode.Trim(),
            SelectedMeetingDevice?.Id,
            SelectedMicrophone?.Id,
            ConsentConfirmed);
    }

    private void NotifyCanStartChanged() => OnPropertyChanged(nameof(CanStart));
}
