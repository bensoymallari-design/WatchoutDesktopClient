using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Watchout.Desktop.Gpu;

/// <summary>
/// Hardware NV12/P010/YUY2 → BGRA on the GPU (D3D11 video processor).
/// Same idea as Resolume: the DXVA surface never becomes an RGB32 byte[].
/// </summary>
sealed class GpuVideoConvert : IDisposable
{
    readonly GpuDevice _gpu;
    readonly ID3D11VideoDevice _video;
    readonly ID3D11VideoContext _videoCtx;
    ID3D11VideoProcessorEnumerator? _enum;
    ID3D11VideoProcessor? _processor;
    int _w, _h;

    GpuVideoConvert(GpuDevice gpu, ID3D11VideoDevice video, ID3D11VideoContext videoCtx)
    {
        _gpu = gpu;
        _video = video;
        _videoCtx = videoCtx;
    }

    public static GpuVideoConvert? TryCreate(GpuDevice gpu)
    {
        try
        {
            var video = gpu.Device.QueryInterface<ID3D11VideoDevice>();
            var videoCtx = gpu.Context.QueryInterface<ID3D11VideoContext>();
            return new GpuVideoConvert(gpu, video, videoCtx);
        }
        catch
        {
            return null;
        }
    }

    public bool Blit(ID3D11Texture2D source, uint arraySlice, ID3D11Texture2D dest)
    {
        var srcDesc = source.Description;
        var dstDesc = dest.Description;
        if (srcDesc.Width < 2 || srcDesc.Height < 2 || dstDesc.Width < 2 || dstDesc.Height < 2)
            return false;
        _gpu.Enter();
        try
        {
            if (IsBgra(srcDesc.Format) && IsBgra(dstDesc.Format) && srcDesc.Format == dstDesc.Format
                && srcDesc.Width == dstDesc.Width && srcDesc.Height == dstDesc.Height)
            {
                _gpu.Context.CopyResource(dest, source);
                return true;
            }
            Ensure((int)dstDesc.Width, (int)dstDesc.Height);
            if (_processor is null || _enum is null) return false;
            _video.CreateVideoProcessorInputView(source, _enum, new VideoProcessorInputViewDescription
            {
                FourCC = 0,
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = arraySlice },
            }, out var input);
            _video.CreateVideoProcessorOutputView(dest, _enum, new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
            }, out var output);
            try
            {
                _videoCtx.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
                var stream = new VideoProcessorStream
                {
                    Enable = true,
                    OutputIndex = 0,
                    InputFrameOrField = 0,
                    InputSurface = input,
                };
                _videoCtx.VideoProcessorBlt(_processor, output, 0, 1, [stream]);
                return true;
            }
            finally
            {
                output.Dispose();
                input.Dispose();
            }
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

    public static bool IsBgra(Format format) =>
        format is Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb or Format.B8G8R8X8_UNorm;

    void Ensure(int w, int h)
    {
        if (_processor is not null && _w == w && _h == h) return;
        _processor?.Dispose();
        _enum?.Dispose();
        _processor = null;
        _enum = null;
        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)w,
            InputHeight = (uint)h,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)w,
            OutputHeight = (uint)h,
            Usage = VideoUsage.PlaybackNormal,
        };
        _video.CreateVideoProcessorEnumerator(content, out _enum);
        _video.CreateVideoProcessor(_enum, 0, out _processor);
        _w = w;
        _h = h;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _enum?.Dispose();
        _videoCtx.Dispose();
        _video.Dispose();
    }
}
