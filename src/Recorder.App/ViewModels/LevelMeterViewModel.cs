using CommunityToolkit.Mvvm.ComponentModel;

namespace Recorder.App.ViewModels;

public partial class LevelMeterViewModel : ObservableObject
{
    [ObservableProperty]
    private float _peakDbfs = -60f;

    [ObservableProperty]
    private float _rmsDbfs = -60f;

    [ObservableProperty]
    private string _label = string.Empty;
}
