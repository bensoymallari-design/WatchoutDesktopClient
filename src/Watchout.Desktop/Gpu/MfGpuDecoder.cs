using System.Runtime.InteropServices;
using NAudio.Wave;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Watchout.Core.Gpu;
using Watchout.Core.Playback;

namespace Watchout.Desktop.Gpu;

sealed class MfGpuDecoder : IDisposable
{
    readonly object _gate = new();
    readonly string _url;
    readonly GpuDevice? _gpu;
    IMFSourceReader? _reader;
    Thread? _thread;
    CancellationTokenSource? _cts;
    GpuVideoConvert? _convert;
    ID3D11Texture2D? _gpuA;
    ID3D11Texture2D? _gpuB;
    ID3D11Texture2D? _gpuFront;
    bool _gpuUseA = true;
    byte[]? _pixels;
    byte[]? _audioScratch;
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
    bool _dxgi;
    bool _gpuSurfaces;
    BufferedWaveProvider? _pcm;
    IWavePlayer? _wave;
    DateTime _lastFrameUtc = DateTime.UtcNow;
    DateTime _openedUtc = DateTime.UtcNow;
    bool _openFinished;
    string? _error;
    bool _software;
    bool _fellBackFromNv12;

    public string? Error { get { lock (_gate) return _error; } }
    public bool UsedSoftwareFallback { get { lock (_gate) return _software; } }
    public bool UsedGpuSurfaces { get { lock (_gate) return _gpuSurfaces; } }
    public bool FellBackFromNv12 { get { lock (_gate) return _fellBackFromNv12; } }
    public bool Ready { get { lock (_gate) return _ready; } }
    public double DurationMs { get { lock (_gate) return _durationMs; } }
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

    public MfGpuDecoder(string fileUrl, bool audio, GpuDevice? gpu = null)
    {
        _url = fileUrl;
        _wantAudio = audio;
        _gpu = gpu;
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
        lock (_gate)
        {
            pixels = _pixels ?? [];
            width = _width;
            height = _height;
            stride = _stride;
            dirty = _dirty;
            var ok = _ready && _pixels is not null && _width > 0 && _gpuFront is null;
            if (ok) _dirty = false;
            return ok;
        }
    }

    void Loop()
    {
        try
        {
            Open();
            lock (_gate)
            {
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
                if (!playing) Thread.Sleep(12);
                else Thread.Sleep(8);
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
            DropGpu();
        }
    }

    void Open()
    {
        Exception? last = null;
        if (_gpu?.DxgiManager is not null)
        {
            if (TryOpen(hardware: true, dxgi: true, nv12: true, out last)) return;
            lock (_gate) _fellBackFromNv12 = true;
            if (TryOpen(hardware: true, dxgi: true, nv12: false, out last)) return;
        }
        if (TryOpen(hardware: true, dxgi: false, nv12: false, out last)) return;
        if (OutputViewMath.RetryOpenWithoutHardwareTransforms(last is not null)
            && TryOpen(hardware: false, dxgi: false, nv12: false, out last))
        {
            lock (_gate) _software = true;
            return;
        }
        throw last ?? new InvalidOperationException(
            "Media Foundation could not decode this MP4 as NV12 or RGB32 — use an 8-bit H.264 MP4 (not Dolby Vision / HEVC HDR)");
    }

    bool TryOpen(bool hardware, bool dxgi, bool nv12, out Exception? error)
    {
        error = null;
        IMFAttributes? attrs = null;
        IMFSourceReader? reader = null;
        IMFMediaType? video = null;
        IMFMediaType? current = null;
        GpuVideoConvert? convert = null;
        try
        {
            MfNative.Check(MfNative.MFCreateAttributes(out attrs, 3), "MFCreateAttributes");
            if (dxgi && _gpu?.DxgiManager is { } manager)
            {
                attrs.SetUnknown(MfNative.MfSourceReaderD3DManager, manager);
            }
            else
            {
                attrs.SetUINT32(MfNative.MfSourceReaderEnableVideoProcessing, 1);
            }
            if (hardware)
                attrs.SetUINT32(MfNative.MfReadwriteEnableHardwareTransforms, 1);
            MfNative.Check(MfNative.MFCreateSourceReaderFromURL(_url, attrs, out reader), "MFCreateSourceReaderFromURL");
            reader.SetStreamSelection(MfNative.AllStreams, false);
            reader.SetStreamSelection(MfNative.VideoStream, true);
            MfNative.Check(MfNative.MFCreateMediaType(out video), "video type");
            video.SetGUID(MfNative.MfMtMajorType, MfNative.MfMediaTypeVideo);
            video.SetGUID(MfNative.MfMtSubtype, nv12 ? MfNative.MfVideoFormatNv12 : MfNative.MfVideoFormatRgb32);
            reader.SetCurrentMediaType(MfNative.VideoStream, IntPtr.Zero, video);
            reader.GetCurrentMediaType(MfNative.VideoStream, out current);
            current.GetUINT64(MfNative.MfMtFrameSize, out var packed);
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

            if (dxgi && nv12)
            {
                convert = GpuVideoConvert.TryCreate(_gpu!);
                if (convert is null) return false;
            }

            lock (_gate)
            {
                _reader = reader;
                reader = null;
                _width = Math.Max(2, width);
                _height = Math.Max(2, height);
                _stride = _width * 4;
                _dxgi = dxgi;
                _convert = convert;
                convert = null;
                _software = !hardware;
                if (!dxgi)
                    _pixels = new byte[_stride * _height];
                _durationMs = duration;
            }

            TryOpenAudio(_reader);
            var deadline = DateTime.UtcNow.AddMilliseconds(GpuResidentPath.PrerollBudgetMs);
            for (var i = 0; i < GpuResidentPath.PrerollAttempts && !_ready && DateTime.UtcNow < deadline; i++)
                ReadOne(_reader, preroll: true);
            if (!GpuResidentPath.OpenProducedAFrame(_ready))
            {
                var kind = nv12 ? "NV12 GPU" : dxgi ? "RGB32 GPU" : "RGB32";
                throw new InvalidOperationException($"DXVA {kind} opened {width}×{height} but produced no picture");
            }
            return true;
        }
        catch (Exception ex)
        {
            convert?.Dispose();
            if (_reader is not null)
            {
                try { Marshal.ReleaseComObject(_reader); } catch { /* ignore */ }
                _reader = null;
            }
            _convert?.Dispose();
            _convert = null;
            DropGpuTargets();
            lock (_gate)
            {
                _dxgi = false;
                _ready = false;
                _gpuSurfaces = false;
                _pixels = null;
            }
            error = ex;
            return false;
        }
        finally
        {
            if (current is not null) Marshal.ReleaseComObject(current);
            if (video is not null) Marshal.ReleaseComObject(video);
            if (reader is not null) Marshal.ReleaseComObject(reader);
            if (attrs is not null) Marshal.ReleaseComObject(attrs);
        }
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
            if (_loop || _playing) Seek(reader, 0);
            return;
        }
        if (sample is null)
        {
            Thread.Sleep(4);
            return;
        }
        try
        {
            if (stream == MfNative.AudioStream || stream == 1)
            {
                PushAudio(sample);
                return;
            }
            if (_dxgi && TryPublishGpu(sample, time))
                return;
            if (_convert is not null)
                return;
            CopyCpu(sample, time);
        }
        finally
        {
            Marshal.ReleaseComObject(sample);
        }
    }

    bool TryPublishGpu(IMFSample sample, long time)
    {
        if (_gpu is null) return false;
        if (!TryWrapDxgi(sample, out var src, out var slice) || src is null)
            return false;
        try
        {
            var desc = src.Description;
            var w = Math.Max(2, (int)desc.Width);
            var h = Math.Max(2, (int)desc.Height);
            EnsureGpuTargets(w, h);
            var dest = _gpuUseA ? _gpuA! : _gpuB!;
            var ok = GpuVideoConvert.IsBgra(desc.Format) && desc.Format == Format.B8G8R8A8_UNorm
                ? CopyGpu(src, dest)
                : _convert is not null && _convert.Blit(src, slice, dest);
            if (!ok && _convert is not null)
                ok = _convert.Blit(src, slice, dest);
            if (!ok) return false;
            lock (_gate)
            {
                _gpuFront = dest;
                _gpuUseA = !_gpuUseA;
                _width = w;
                _height = h;
                _stride = w * 4;
                _frameTime100ns = time;
                _ready = true;
                _dirty = true;
                _gpuSurfaces = true;
                _lastFrameUtc = DateTime.UtcNow;
            }
            return true;
        }
        finally
        {
            src.Dispose();
        }
    }

    bool CopyGpu(ID3D11Texture2D src, ID3D11Texture2D dest)
    {
        _gpu!.Enter();
        try
        {
            _gpu.Context.CopyResource(dest, src);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _gpu.Leave();
        }
    }

    void EnsureGpuTargets(int w, int h)
    {
        if (_gpuA is not null
            && _gpuA.Description.Width == (uint)w
            && _gpuA.Description.Height == (uint)h)
            return;
        _gpu!.Enter();
        try
        {
            DropGpuTargets();
            _gpuA = CreateBgra(w, h);
            _gpuB = CreateBgra(w, h);
        }
        finally
        {
            _gpu.Leave();
        }
        if (_convert is null && _gpu is not null)
            _convert = GpuVideoConvert.TryCreate(_gpu);
    }

    ID3D11Texture2D CreateBgra(int w, int h) =>
        _gpu!.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });

    static bool TryWrapDxgi(IMFSample sample, out ID3D11Texture2D? tex, out uint slice)
    {
        tex = null;
        slice = 0;
        sample.GetBufferCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            sample.GetBufferByIndex(i, out var buffer);
            try
            {
                if (TryWrapBuffer(buffer, out tex, out slice)) return true;
            }
            finally
            {
                Marshal.ReleaseComObject(buffer);
            }
        }
        try
        {
            sample.ConvertToContiguousBuffer(out var contig);
            try { return TryWrapBuffer(contig, out tex, out slice); }
            finally { Marshal.ReleaseComObject(contig); }
        }
        catch
        {
            return false;
        }
    }

    static bool TryWrapBuffer(IMFMediaBuffer buffer, out ID3D11Texture2D? tex, out uint slice)
    {
        tex = null;
        slice = 0;
        IMFDXGIBuffer? dxgi = null;
        var unk = Marshal.GetIUnknownForObject(buffer);
        try
        {
            var iid = typeof(IMFDXGIBuffer).GUID;
            var hr = Marshal.QueryInterface(unk, ref iid, out var pDxgi);
            if (hr >= 0 && pDxgi != IntPtr.Zero)
            {
                try { dxgi = (IMFDXGIBuffer)Marshal.GetObjectForIUnknown(pDxgi); }
                finally { Marshal.Release(pDxgi); }
            }
        }
        finally
        {
            Marshal.Release(unk);
        }
        dxgi ??= buffer as IMFDXGIBuffer;
        if (dxgi is null) return false;
        dxgi.GetResource(MfNative.Id3d11Texture2D, out var ptr);
        if (ptr == IntPtr.Zero) return false;
        tex = new ID3D11Texture2D(ptr);
        Marshal.Release(ptr);
        try { dxgi.GetSubresourceIndex(out slice); } catch { slice = 0; }
        return true;
    }

    void CopyCpu(IMFSample sample, long time)
    {
        sample.ConvertToContiguousBuffer(out var buffer);
        buffer.Lock(out var data, out _, out var length);
        try
        {
            lock (_gate)
            {
                if (_pixels is null || _pixels.Length < length)
                    _pixels = new byte[Math.Max(length, _stride * _height)];
                var copy = Math.Min(length, _pixels.Length);
                Marshal.Copy(data, _pixels, 0, copy);
                _stride = _width * 4;
                _frameTime100ns = time;
                _ready = true;
                _dirty = true;
                _lastFrameUtc = DateTime.UtcNow;
            }
        }
        finally
        {
            buffer.Unlock();
            Marshal.ReleaseComObject(buffer);
        }
    }

    void PushAudio(IMFSample sample)
    {
        if (_pcm is null) return;
        sample.ConvertToContiguousBuffer(out var buffer);
        buffer.Lock(out var data, out _, out var length);
        try
        {
            if (!GpuResidentPath.ReuseBuffer(_audioScratch?.Length ?? 0, length))
                _audioScratch = new byte[GpuResidentPath.GrowBuffer(_audioScratch?.Length ?? 0, length)];
            Marshal.Copy(data, _audioScratch!, 0, length);
            _pcm.AddSamples(_audioScratch, 0, length);
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

    void DropGpuTargets()
    {
        ID3D11Texture2D? a;
        ID3D11Texture2D? b;
        lock (_gate)
        {
            _gpuFront = null;
            a = _gpuA;
            b = _gpuB;
            _gpuA = null;
            _gpuB = null;
        }
        a?.Dispose();
        b?.Dispose();
    }

    void DropGpu()
    {
        DropGpuTargets();
        GpuVideoConvert? convert;
        lock (_gate)
        {
            convert = _convert;
            _convert = null;
        }
        convert?.Dispose();
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        if (_thread is { IsAlive: true } && !_thread.Join(400))
            try { _cts?.Cancel(); } catch { /* ignore */ }
        CloseAudio();
        DropGpu();
        _cts?.Dispose();
    }
}
