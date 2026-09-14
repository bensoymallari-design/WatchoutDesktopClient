using System.Windows.Media.Imaging;
using Watchout.Desktop.Media;

namespace Watchout.Desktop.Views;

public sealed class CaptureLayer : Grid
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly TextBlock _status = new()
    {
        Text = "NO SIGNAL\nStart the live source",
        Foreground = Brushes.White,
        FontSize = 18,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };
    string? _deviceId;
    string? _ndiName;
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

    public string? NdiName
    {
        get => _ndiName;
        set
        {
            if (_ndiName == value) return;
            Detach();
            _ndiName = value;
            _status.Text = "NO SIGNAL\nWaiting for NDI…";
            UpdateOverlay(null);
            if (IsLoaded) Attach();
        }
    }

    void Attach()
    {
        if (_listening) return;
        if (!string.IsNullOrEmpty(_deviceId))
        {
            var bmp = CaptureHub.Retain(_deviceId, _onFrame);
            _listening = true;
            UpdateOverlay(bmp);
            return;
        }
        if (!string.IsNullOrEmpty(_ndiName))
        {
            var bmp = NdiHub.Retain(_ndiName, _onFrame);
            _listening = true;
            UpdateOverlay(bmp);
        }
    }

    void Detach()
    {
        if (!_listening) return;
        if (!string.IsNullOrEmpty(_deviceId)) CaptureHub.Release(_deviceId, _onFrame);
        if (!string.IsNullOrEmpty(_ndiName)) NdiHub.Release(_ndiName, _onFrame);
        _listening = false;
        _image.Source = null;
        _status.Visibility = Visibility.Visible;
    }

    void OnFrame()
    {
        if (!string.IsNullOrEmpty(_deviceId))
        {
            UpdateOverlay(CaptureHub.Peek(_deviceId));
            return;
        }
        if (!string.IsNullOrEmpty(_ndiName))
            UpdateOverlay(NdiHub.Peek(_ndiName));
    }

    void UpdateOverlay(WriteableBitmap? bmp)
    {
        if (bmp is not null) _image.Source = bmp;
        _status.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
