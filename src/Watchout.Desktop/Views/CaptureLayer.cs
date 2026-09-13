using System.Windows.Media.Imaging;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class CaptureLayer : Grid
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly TextBlock _status = new()
    {
        Text = "NO SIGNAL\nStart Resolume or another HDMI/SDI source",
        Foreground = Brushes.White,
        FontSize = 18,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };
    string? _deviceId;
    bool _listening;
    readonly Action _onFrame;

    public CaptureLayer()
    {
        Background = new SolidColorBrush(Color.FromRgb(8, 16, 24));
        _onFrame = OnFrame;
        Children.Add(_image);
        Children.Add(_status);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public string? DeviceId
    {
        get => _deviceId;
        set
        {
            if (_deviceId == value) return;
            Detach();
            _deviceId = value;
            UpdateOverlay(null);
            if (IsLoaded) Attach();
        }
    }

    void Attach()
    {
        if (_listening || string.IsNullOrEmpty(_deviceId)) return;
        var bmp = CaptureHub.Retain(_deviceId, _onFrame);
        _listening = true;
        UpdateOverlay(bmp);
    }

    void Detach()
    {
        if (!_listening || _deviceId is null) return;
        CaptureHub.Release(_deviceId, _onFrame);
        _listening = false;
        _image.Source = null;
        _status.Visibility = Visibility.Visible;
    }

    void OnFrame()
    {
        if (_deviceId is null) return;
        UpdateOverlay(CaptureHub.Peek(_deviceId));
    }

    void UpdateOverlay(WriteableBitmap? bmp)
    {
        if (bmp is not null) _image.Source = bmp;
        _status.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
