using System.Runtime.InteropServices;
using NAudio.Wave;
using Watchout.Core.Playback;

namespace Watchout.Desktop.Gpu;

sealed class MfGpuDecoder : IDisposable
{
    readonly object _gate = new();
    readonly string _url;
    IMFSourceReader? _reader;
    Thread? _thread;
    CancellationTokenSource? _cts;
    byte[]? _pixels;
    int _width;
    int _height;
    int _stride;
    long _frameTime100ns;
    double _durationMs;
    bool _dirty;
    bool _ready;
    bool _loop;
    bool _playing;
    double _targetMs;
    double _volume = 1;
    bool _wantAudio;
    BufferedWaveProvider? _pcm;
    IWavePlayer? _wave;
    string? _error;

    public string? Error { get { lock (_gate) return _error; } }
    public bool Ready { get { lock (_gate) return _ready; } }
    public double DurationMs { get { lock (_gate) return _durationMs; } }

    public MfGpuDecoder(string fileUrl, bool audio)
    {
        _url = fileUrl;
        _wantAudio = audio;
        _cts = new CancellationTokenSource();
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "WatchMe DXVA " + Path.GetFileName(fileUrl),
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
                    if (_wave.PlaybackState != NAudio.Wave.PlaybackState.Playing) _wave.Play();
                }
                else if (_wave.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                    _wave.Pause();
            }
        }
    }

    public bool TryCopyFrame(out byte[] pixels, out int width, out int height, out int stride, out bool dirty)
    {
        lock (_gate)
        {
            pixels = _pixels ?? [];
            width = _width;
            height = _height;
            stride = _stride;
            dirty = _dirty;
            _dirty = false;
            return _ready && _pixels is not null && _width > 0;
        }
    }

    void Loop()
    {
        try
        {
            Open();
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
                if (!playing) Thread.Sleep(12);
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _error = ex.Message;
        }
        finally
        {
            CloseAudio();
            if (_reader is not null) Marshal.ReleaseComObject(_reader);
            _reader = null;
        }
    }

    void Open()
    {
        MfNative.Check(MfNative.MFCreateAttributes(out var attrs, 2), "MFCreateAttributes");
        attrs.SetUINT32(MfNative.MfSourceReaderEnableVideoProcessing, 1);
        attrs.SetUINT32(MfNative.MfReadwriteEnableHardwareTransforms, 1);
        MfNative.Check(MfNative.MFCreateSourceReaderFromURL(_url, attrs, out var reader), "MFCreateSourceReaderFromURL");
        Marshal.ReleaseComObject(attrs);
        reader.SetStreamSelection(MfNative.AllStreams, false);
        reader.SetStreamSelection(MfNative.VideoStream, true);
        MfNative.Check(MfNative.MFCreateMediaType(out var video), "video type");
        video.SetGUID(MfNative.MfMtMajorType, MfNative.MfMediaTypeVideo);
        video.SetGUID(MfNative.MfMtSubtype, MfNative.MfVideoFormatRgb32);
        reader.SetCurrentMediaType(MfNative.VideoStream, IntPtr.Zero, video);
        Marshal.ReleaseComObject(video);
        reader.GetCurrentMediaType(MfNative.VideoStream, out var current);
        current.GetUINT64(MfNative.MfMtFrameSize, out var packed);
        Marshal.ReleaseComObject(current);
        var width = (int)(packed >> 32);
        var height = (int)(packed & 0xFFFFFFFF);
        var duration = 0.0;
        try
        {
            reader.GetPresentationAttribute(unchecked((int)0xFFFFFFFF), MfNative.MfPdDuration, out var dur);
            if (dur.vt == MfNative.VtI8 && dur.hVal > 0)
                duration = dur.hVal / 10_000.0;
        }
        catch { /* optional */ }

        lock (_gate)
        {
            _reader = reader;
            _width = Math.Max(2, width);
            _height = Math.Max(2, height);
            _stride = _width * 4;
            _pixels = new byte[_stride * _height];
            _durationMs = duration;
        }

        TryOpenAudio(reader);
        ReadOne(reader, preroll: true);
    }

    void TryOpenAudio(IMFSourceReader reader)
    {
        try
        {
            reader.SetStreamSelection(MfNative.AudioStream, true);
            MfNative.Check(MfNative.MFCreateMediaType(out var audio), "audio type");
            audio.SetGUID(MfNative.MfMtMajorType, MfNative.MfMediaTypeAudio);
            audio.SetGUID(MfNative.MfMtSubtype, MfNative.MfAudioFormatPcm);
            audio.SetUINT32(MfNative.MfMtAudioNumChannels, 2);
            audio.SetUINT32(MfNative.MfMtAudioSamplesPerSecond, 48000);
            audio.SetUINT32(MfNative.MfMtAudioBitsPerSample, 16);
            audio.SetUINT32(MfNative.MfMtAudioBlockAlignment, 4);
            audio.SetUINT32(MfNative.MfMtAudioAvgBytesPerSecond, 48000 * 4);
            reader.SetCurrentMediaType(MfNative.AudioStream, IntPtr.Zero, audio);
            Marshal.ReleaseComObject(audio);
            _pcm = new BufferedWaveProvider(new WaveFormat(48000, 16, 2))
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(1),
            };
            var wave = Watchout.Desktop.Engine.AudioEngine.CreatePlayer();
            wave.Init(_pcm);
            _wave = wave;
        }
        catch
        {
            try { reader.SetStreamSelection(MfNative.AudioStream, false); } catch { /* no audio */ }
        }
    }

    void Pump(double targetMs, bool playing, bool loop)
    {
        var reader = _reader;
        if (reader is null) return;
        var pos = _frameTime100ns / 10_000.0;
        if (!playing)
        {
            if (Math.Abs(pos - targetMs) > VideoSync.ScrubSeekMs)
                Seek(reader, targetMs);
            if (!_ready) ReadOne(reader, preroll: true);
            return;
        }
        if (targetMs + 80 < pos && pos - targetMs > VideoSync.PlayReseekMs)
            Seek(reader, targetMs);
        ReadOne(reader, preroll: false);
        if (_ready)
        {
            pos = _frameTime100ns / 10_000.0;
            if (loop && _durationMs > 1 && pos >= _durationMs - 40)
                Seek(reader, 0);
        }
    }

    void Seek(IMFSourceReader reader, double ms)
    {
        var pv = new MfNative.PropVariant { vt = MfNative.VtI8, hVal = (long)Math.Max(0, ms) * 10_000 };
        try { reader.SetCurrentPosition(Guid.Empty, pv); } catch { /* not seekable */ }
        lock (_gate) _frameTime100ns = pv.hVal;
    }

    void ReadOne(IMFSourceReader reader, bool preroll)
    {
        reader.ReadSample(preroll ? MfNative.VideoStream : MfNative.AllStreams, 0, out var stream, out var flags, out var time, out var sample);
        if ((flags & MfNative.EndOfStream) != 0)
        {
            if (sample is not null) Marshal.ReleaseComObject(sample);
            if (_loop) Seek(reader, 0);
            return;
        }
        if (sample is null) return;
        try
        {
            if (stream == MfNative.AudioStream || stream == 1)
            {
                PushAudio(sample);
                return;
            }
            sample.ConvertToContiguousBuffer(out var buffer);
            buffer.Lock(out var data, out _, out var length);
            try
            {
                lock (_gate)
                {
                    if (_pixels is null) return;
                    var copy = Math.Min(length, _pixels.Length);
                    Marshal.Copy(data, _pixels, 0, copy);
                    _stride = _width * 4;
                    _frameTime100ns = time;
                    _ready = true;
                    _dirty = true;
                }
            }
            finally
            {
                buffer.Unlock();
                Marshal.ReleaseComObject(buffer);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(sample);
        }
    }

    void PushAudio(IMFSample sample)
    {
        if (_pcm is null) return;
        sample.ConvertToContiguousBuffer(out var buffer);
        buffer.Lock(out var data, out _, out var length);
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, length);
            _pcm.AddSamples(bytes, 0, bytes.Length);
        }
        finally
        {
            buffer.Unlock();
            Marshal.ReleaseComObject(buffer);
        }
    }

    void CloseAudio()
    {
        try { _wave?.Stop(); } catch { /* ignore */ }
        _wave?.Dispose();
        _wave = null;
        _pcm = null;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        if (_thread is { IsAlive: true } && !_thread.Join(400))
            try { _cts?.Cancel(); } catch { /* ignore */ }
        CloseAudio();
        _cts?.Dispose();
    }
}
