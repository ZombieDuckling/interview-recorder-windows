using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Recorder.App.ViewModels;

namespace Recorder.App.Views;

public partial class TranscriptEditorView : UserControl
{
    private readonly MediaPlayer _mediaPlayer = new();
    private TranscriptEditorViewModel? _viewModel;
    private bool _isMediaOpen;

    public TranscriptEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        _mediaPlayer.MediaOpened += OnMediaOpened;
        _mediaPlayer.MediaFailed += (_, args) =>
        {
            _isMediaOpen = false;
            if (_viewModel is not null)
            {
                _viewModel.IsPlaying = false;
            }

            _ = args;
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as TranscriptEditorViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        OpenAudioIfNeeded();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(TranscriptEditorViewModel.SelectedSegment))
        {
            SeekToSelectedSegment();
        }
        else if (e.PropertyName == nameof(TranscriptEditorViewModel.IsPlaying))
        {
            if (_viewModel.IsPlaying)
            {
                OpenAudioIfNeeded();
                _mediaPlayer.Play();
            }
            else
            {
                _mediaPlayer.Pause();
            }
        }
    }

    private void OpenAudioIfNeeded()
    {
        if (_viewModel is null || _isMediaOpen)
        {
            return;
        }

        if (!File.Exists(_viewModel.AudioPath))
        {
            return;
        }

        _mediaPlayer.Open(new Uri(_viewModel.AudioPath, UriKind.Absolute));
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        _isMediaOpen = true;
        SeekToSelectedSegment();
    }

    private void SeekToSelectedSegment()
    {
        if (_viewModel?.SelectedSegment is null || !_isMediaOpen)
        {
            return;
        }

        _mediaPlayer.Position = TimeSpan.FromSeconds(_viewModel.SelectedSegment.StartSeconds);
        if (_viewModel.IsPlaying)
        {
            _mediaPlayer.Play();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _mediaPlayer.Close();
        _isMediaOpen = false;
    }
}
