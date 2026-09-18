using NAudio.Wave;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Watchout.Core.Gpu;
using Watchout.Core.Media;
using Watchout.Core.Playback;

namespace Watchout.Desktop.Gpu;

/// <summary>
/// HAP Q is Snappy → DXT uploaded as a BC texture. No Media Foundation,
/// no RGB32 RAM copy. Only used when the clip is already HAP.
/// </summary>
sealed class HapGpuDecoder : IGpuFileDecoder
{
    readonly object _gate = new();
    readonly string _path;
    readonly GpuDevice? _gpu;
    HapMovie? _movie;
    Thread? _thread;
    CancellationTokenSource? _cts;
    ID3D11Texture2D? _gpuA;
    ID3D11Texture2D? _gpuB;
    ID3D11Texture2D? _gpuFront;
    bool _gpuUseA = true;
    bool _dirty;
    bool _ready;
    bool _loop;
    bool _playing;
    double _targetMs;
    double _volume = 1;
    bool _wantAudio;
    bool _hapQ;
    DateTime _lastFrameUtc = DateTime.UtcNow;
    DateTime _openedUtc = DateTime.UtcNow;
    bool _openFinished;
    string? _error;
    int _index = -1;
    AudioFileReader? _audio;
    IWavePlayer? _wave;

    public string? Error { get { lock (_gate) return _error; } }
    public bool UsedSoftwareFallback => false;
    public bool UsedGpuSurfaces => true;
    public bool FellBackFromNv12 => false;
    public bool Ready { get { lock (_gate) return _ready; } }
    public bool Opening { get { lock (_gate) return !_openFinished && _error is null; } }
    public bool HapYCoCg { get { lock (_gate) return _hapQ; } }
    public bool Dead
    {
        get
        {
            lock (_gate)
            {
                if (_error is not null) return true;
                return _thread is { IsAlive: false };
            }
        }
    }
    public bool Stalled
    {
        get
        {
            lock (_gate)
            {
                if (!_playing) return false;
                if (GpuResidentPath.StillOpening(_openFinished)) return false;
                var sinceOpen = (DateTime.UtcNow - _openedUtc).TotalMilliseconds;
                if (GpuResidentPath.StallWithoutPicture(true, _ready, sinceOpen, VideoSync.StallMs))
                    return true;
                if (!_ready) return false;
                return (DateTime.UtcNow - _lastFrameUtc).TotalMilliseconds >= VideoSync.StallMs;
            }
        }
    }

    public HapGpuDecoder(string path, bool audio, GpuDevice? gpu)
    {
        _path = path;
        _wantAudio = audio;
        _gpu = gpu;
        _cts = new CancellationTokenSource();
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "WatchMe HAP " + Path.GetFileName(path),
        };
        _thread.Start();
    }

    public void Sync(double mediaMs, bool playing, bool loop, float volume, bool audio)
    {
        lock (_gate)
        {
            _targetMs = mediaMs;
            _playing = playing;
            _loop = loop;
            _volume = volume;
            _wantAudio = audio;
            if (_wave is not null)
            {
                try { _wave.Volume = Math.Clamp(volume, 0, 1); } catch { /* driver */ }
                if (playing && audio && volume > 0.001f)
                {
                    if (_wave.PlaybackState != PlaybackState.Playing) _wave.Play();
                }
                else if (_wave.PlaybackState == PlaybackState.Playing)
                    _wave.Pause();
            }
        }
    }

    public bool TryBindGpu(out ID3D11Texture2D? texture, out bool dirty)
    {
        lock (_gate)
        {
            texture = _gpuFront;
            dirty = _dirty;
            var ok = _ready && _gpuFront is not null;
            if (ok) _dirty = false;
            return ok;
        }
    }

    public bool TryCopyFrame(out byte[] pixels, out int width, out int height, out int stride, out bool dirty)
    {
        pixels = [];
        width = 0;
        height = 0;
        stride = 0;
        dirty = false;
        return false;
    }

    void Loop()
    {
        try
        {
            _movie = HapMovie.Open(_path);
            OpenAudio();
            lock (_gate)
            {
                _hapQ = _movie.Kind == HapTextureKind.YCoCgDxt5;
                _openFinished = true;
                _openedUtc = DateTime.UtcNow;
                _lastFrameUtc = _openedUtc;
            }
            var token = _cts!.Token;
            while (!token.IsCancellationRequested)
            {
                double target;
                bool playing;
                bool loop;
                lock (_gate)
                {
                    target = _targetMs;
                    playing = _playing;
                    loop = _loop;
                }
                Pump(target, playing, loop);
                Thread.Sleep(playing ? 8 : 16);
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _error = ex.Message;
        }
        finally
        {
            try { _wave?.Stop(); } catch { /* ignore */ }
            _wave?.Dispose();
            _audio?.Dispose();
            DropGpu();
            _movie?.Dispose();
        }
    }

    void Pump(double targetMs, bool playing, bool loop)
    {
        if (_movie is null || _gpu is null) return;
        var duration = _movie.DurationMs > 1 ? _movie.DurationMs : 1;
        var time = loop ? targetMs % duration : Math.Clamp(targetMs, 0, duration);
        if (!playing && _index >= 0) return;
        var index = _movie.SampleIndexAt(time);
        if (index == _index) return;
        var packet = _movie.ReadSample(index);
        var frame = HapCodec.Decode(packet, _movie.Width, _movie.Height);
        Upload(frame);
        _index = index;
    }

    void Upload(HapFrame frame)
    {
        if (_gpu is null) return;
        var format = frame.Kind switch
        {
            HapTextureKind.Dxt1 => Format.BC1_UNorm,
            HapTextureKind.Rgtc1 => Format.BC4_UNorm,
            _ => Format.BC3_UNorm,
        };
        _gpu.Enter();
        try
        {
            EnsureTargets(frame.Width, frame.Height, format);
            var dest = _gpuUseA ? _gpuA! : _gpuB!;
            var pitch = Math.Max(1, (frame.Width + 3) / 4) * frame.BlockBytes;
            unsafe
            {
                fixed (byte* p = frame.Blocks)
                    _gpu.Context.UpdateSubresource(dest, 0, null, (IntPtr)p, (uint)pitch, 0);
            }
        }
        finally
        {
            _gpu.Leave();
        }
        lock (_gate)
        {
            _gpuFront = _gpuUseA ? _gpuA : _gpuB;
            _gpuUseA = !_gpuUseA;
            _ready = true;
            _dirty = true;
            _hapQ = frame.YCoCg;
            _lastFrameUtc = DateTime.UtcNow;
        }
    }

    void EnsureTargets(int w, int h, Format format)
    {
        if (_gpuA is not null
            && _gpuA.Description.Width == (uint)w
            && _gpuA.Description.Height == (uint)h
            && _gpuA.Description.Format == format)
            return;
        DropGpuTargets();
        _gpuA = CreateBc(w, h, format);
        _gpuB = CreateBc(w, h, format);
    }

    ID3D11Texture2D CreateBc(int w, int h, Format format) =>
        _gpu!.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
        });

    void OpenAudio()
    {
        if (!_wantAudio) return;
        try
        {
            _audio = new AudioFileReader(_path);
            var wave = new WasapiOut();
            wave.Init(_audio);
            _wave = wave;
        }
        catch
        {
            _audio?.Dispose();
            _audio = null;
        }
    }

    void DropGpu()
    {
        _gpu?.Enter();
        try { DropGpuTargets(); }
        finally { _gpu?.Leave(); }
    }

    void DropGpuTargets()
    {
        _gpuFront = null;
        _gpuA?.Dispose();
        _gpuB?.Dispose();
        _gpuA = null;
        _gpuB = null;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _thread?.Join(500); } catch { /* ignore */ }
        _cts?.Dispose();
    }
}
