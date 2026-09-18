using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Watchout.Core.Gpu;
using Watchout.Core.Models;
using Watchout.Core.Stage;

namespace Watchout.Desktop.Gpu;

sealed class GpuCompositor : IDisposable
{
    static readonly string Hlsl = """
        cbuffer Layer : register(b0)
        {
            float4x4 Color;
            float4 ChromaKey;
            float4 Wipe;
            float Opacity;
            float ChromaOn;
            float Edge;
            float HapQ;
        };
        struct VSIn { float2 pos : POSITION; float2 uv : TEXCOORD0; };
        struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        Texture2D tex : register(t0);
        SamplerState samp : register(s0);
        VSOut VS(VSIn i)
        {
            VSOut o;
            o.pos = float4(i.pos, 0, 1);
            o.uv = i.uv;
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            float4 c = tex.Sample(samp, i.uv);
            if (HapQ > 0.5)
            {
                float Co = (c.r - 0.5) * (c.b * (255.0 / 8.0) + 1.0);
                float Cg = (c.g - 0.5) * (c.b * (255.0 / 8.0) + 1.0);
                float Y = c.a;
                c = float4(saturate(Y + Co - Cg), saturate(Y + Cg), saturate(Y - Co - Cg), 1);
            }
            if (ChromaOn > 0.5)
            {
                float d = distance(c.rgb, ChromaKey.rgb);
                c.a *= saturate((d - ChromaKey.a * 0.45) / max(0.02, ChromaKey.a));
            }
            float t = Wipe.x / 100.0;
            if (t < 0.994)
            {
                float ang = Wipe.y * 0.01745329251;
                float2 dir = float2(cos(ang), sin(ang));
                float along = saturate(dot(i.uv - 0.5, dir) + 0.5);
                float f = saturate(Wipe.z / 200.0);
                float mask = smoothstep(t - f, t + f, along);
                c.a *= 1 - mask;
            }
            float3 rgb = mul(Color, float4(c.rgb, 1)).rgb;
            return float4(rgb, c.a * Opacity * Edge);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    struct LayerCbuffer
    {
        public Matrix4x4 Color;
        public Color4 ChromaKey;
        public Color4 Wipe;
        public float Opacity;
        public float ChromaOn;
        public float Edge;
        public float HapQ;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Vert
    {
        public Vector2 Pos;
        public Vector2 Uv;
    }

    readonly GpuDevice _gpu;
    readonly ID3D11VertexShader _vs;
    readonly ID3D11PixelShader _ps;
    readonly ID3D11InputLayout _layout;
    readonly ID3D11Buffer _vb;
    readonly ID3D11Buffer _cb;
    readonly ID3D11SamplerState _sampler;
    readonly ID3D11RasterizerState _raster;
    readonly ID3D11DepthStencilState _depth;
    readonly ID3D11BlendState[] _blend;
    readonly Dictionary<string, ID3D11Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ID3D11ShaderResourceView> _srvs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, bool> _hapQ = new(StringComparer.OrdinalIgnoreCase);
    ID3D11Texture2D? _stageTex;
    ID3D11Texture2D? _stageStaging;
    ID3D11RenderTargetView? _stageRtv;
    int _stageW, _stageH;
    byte[]? _stageBits;

    public GpuCompositor(GpuDevice gpu)
    {
        _gpu = gpu;
        var vsBlob = Compile("VS", "vs_4_0");
        var psBlob = Compile("PS", "ps_4_0");
        _vs = gpu.Device.CreateVertexShader(vsBlob);
        _ps = gpu.Device.CreatePixelShader(psBlob);
        _layout = gpu.Device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
        ], vsBlob);
        vsBlob.Dispose();
        psBlob.Dispose();
        _vb = gpu.Device.CreateBuffer((uint)(6 * Marshal.SizeOf<Vert>()), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _cb = gpu.Device.CreateBuffer((uint)Marshal.SizeOf<LayerCbuffer>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _sampler = gpu.Device.CreateSamplerState(SamplerDescription.LinearWrap);
        _raster = gpu.Device.CreateRasterizerState(RasterizerDescription.CullNone);
        _depth = gpu.Device.CreateDepthStencilState(DepthStencilDescription.None);
        _blend =
        [
            gpu.Device.CreateBlendState(BlendDescription.NonPremultiplied),
            gpu.Device.CreateBlendState(BlendDescription.Additive),
            gpu.Device.CreateBlendState(new BlendDescription(Blend.DestinationColor, Blend.InverseSourceAlpha)),
            gpu.Device.CreateBlendState(new BlendDescription(Blend.One, Blend.InverseSourceColor)),
        ];
    }

    static Blob Compile(string entry, string profile)
    {
        var bytes = Encoding.ASCII.GetBytes(Hlsl);
        unsafe
        {
            fixed (byte* p = bytes)
            {
                var hr = Compiler.Compile(
                    p,
                    (PointerUSize)(uint)bytes.Length,
                    "WatchMeLayer.hlsl",
                    null,
                    null,
                    entry,
                    profile,
                    ShaderFlags.OptimizationLevel3,
                    EffectFlags.None,
                    out var blob,
                    out var errors);
                if (hr.Failure || blob is null)
                {
                    var msg = errors is null ? hr.ToString() : Encoding.UTF8.GetString(errors.AsBytes());
                    errors?.Dispose();
                    throw new InvalidOperationException("HLSL " + entry + ": " + msg);
                }
                errors?.Dispose();
                return blob;
            }
        }
    }

    public void UploadBgra(string key, byte[] pixels, int width, int height, int stride)
    {
        if (pixels.Length < stride || width < 2 || height < 2) return;
        unsafe
        {
            fixed (byte* p = pixels)
                UploadBgra(key, (IntPtr)p, width, height, stride);
        }
    }

    public void UploadBgra(string key, IntPtr pixels, int width, int height, int stride)
    {
        if (pixels == IntPtr.Zero || width < 2 || height < 2 || stride < width * 4) return;
        var tex = EnsureBgra(key, width, height);
        _gpu.Enter();
        try { _gpu.Context.UpdateSubresource(tex, 0, null, pixels, (uint)stride, (uint)(stride * height)); }
        finally { _gpu.Leave(); }
    }

    /// <summary>
    /// GPU→GPU copy of a DXVA/BGRA surface. No RGB32 trip through system RAM.
    /// </summary>
    public void BindGpu(string key, ID3D11Texture2D source, bool hapQ = false)
    {
        var desc = source.Description;
        var w = (int)desc.Width;
        var h = (int)desc.Height;
        if (w < 2 || h < 2) return;
        if (IsBc(desc.Format))
        {
            var tex = Ensure(key, w, h, desc.Format);
            _hapQ[key] = hapQ;
            _gpu.Enter();
            try { _gpu.Context.CopyResource(tex, source); }
            finally { _gpu.Leave(); }
            return;
        }
        _hapQ.Remove(key);
        var bgra = EnsureBgra(key, w, h);
        _gpu.Enter();
        try { _gpu.Context.CopyResource(bgra, source); }
        finally { _gpu.Leave(); }
    }

    static bool IsBc(Format format) =>
        format is Format.BC1_UNorm or Format.BC3_UNorm or Format.BC4_UNorm;

    ID3D11Texture2D Ensure(string key, int width, int height, Format format)
    {
        if (_textures.TryGetValue(key, out var tex)
            && tex.Description.Width == (uint)width
            && tex.Description.Height == (uint)height
            && tex.Description.Format == format)
            return tex;
        tex?.Dispose();
        if (_srvs.Remove(key, out var oldSrv)) oldSrv.Dispose();
        tex = _gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = format is Format.B8G8R8A8_UNorm
                ? BindFlags.ShaderResource | BindFlags.RenderTarget
                : BindFlags.ShaderResource,
        });
        _textures[key] = tex;
        _srvs[key] = _gpu.Device.CreateShaderResourceView(tex);
        return tex;
    }

    ID3D11Texture2D EnsureBgra(string key, int width, int height) =>
        Ensure(key, width, height, Format.B8G8R8A8_UNorm);

    public bool Has(string key) => _srvs.ContainsKey(key);

    public void Drop(string key)
    {
        if (_textures.Remove(key, out var tex)) tex.Dispose();
        if (_srvs.Remove(key, out var srv)) srv.Dispose();
        _hapQ.Remove(key);
    }

    public void Render(ID3D11RenderTargetView rtv, int width, int height, IReadOnlyList<GpuDraw> draws, Display? outputDisplay)
    {
        _gpu.Enter();
        try
        {
            RenderCore(rtv, width, height, draws, outputDisplay);
        }
        finally
        {
            _gpu.Leave();
        }
    }

    void RenderCore(ID3D11RenderTargetView rtv, int width, int height, IReadOnlyList<GpuDraw> draws, Display? outputDisplay)
    {
        var ctx = _gpu.Context;
        ctx.OMSetRenderTargets(rtv);
        ctx.RSSetViewport(new Viewport(0, 0, width, height));
        var ready = 0;
        foreach (var draw in draws)
            if (_srvs.ContainsKey(draw.SourceKey)) ready++;
        if (!GpuSourceLifetime.ClearToBlack(ready))
            return;
        ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.IASetInputLayout(_layout);
        ctx.IASetVertexBuffer(0, _vb, (uint)Marshal.SizeOf<Vert>());
        ctx.VSSetShader(_vs);
        ctx.PSSetShader(_ps);
        ctx.PSSetSampler(0, _sampler);
        ctx.RSSetState(_raster);
        ctx.OMSetDepthStencilState(_depth);
        var edge = outputDisplay is { Blend: true }
            ? GpuLayerMath.EdgeBlend(0.5f, true, (float)outputDisplay.BlendWidth, width)
            : 1f;
        foreach (var draw in draws)
        {
            if (!_srvs.TryGetValue(draw.SourceKey, out var srv)) continue;
            var blend = draw.Blend switch
            {
                BlendMode.Add => _blend[1],
                BlendMode.Multiply => _blend[2],
                BlendMode.Screen => _blend[3],
                _ => _blend[0],
            };
            ctx.OMSetBlendState(blend);
            ctx.PSSetShaderResource(0, srv);
            WriteQuad(draw, width, height, outputDisplay);
            WriteCbuffer(draw, outputDisplay is { Blend: true } d
                ? GpuLayerMath.EdgeBlend(0.5f, true, (float)d.BlendWidth, width)
                : 1);
            ctx.Draw(6, 0);
        }
        _ = edge;
    }

    public WriteableBitmapPresent RenderStage(int width, int height, IReadOnlyList<GpuDraw> draws)
    {
        width = Math.Max(2, width);
        height = Math.Max(2, height);
        if (_stageTex is null || _stageW != width || _stageH != height)
        {
            _stageRtv?.Dispose();
            _stageTex?.Dispose();
            _stageStaging?.Dispose();
            _stageTex = _gpu.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            _stageRtv = _gpu.Device.CreateRenderTargetView(_stageTex);
            _stageStaging = _gpu.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read,
            });
            _stageW = width;
            _stageH = height;
            _stageBits = new byte[width * height * 4];
        }
        Render(_stageRtv!, width, height, draws, null);
        _gpu.Enter();
        try
        {
            _gpu.Context.CopyResource(_stageStaging!, _stageTex);
            var mapped = _gpu.Context.Map(_stageStaging!, 0, MapMode.Read);
            try
            {
                var dest = _stageBits!;
                var row = width * 4;
                for (var y = 0; y < height; y++)
                    Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, dest, y * row, row);
            }
            finally
            {
                _gpu.Context.Unmap(_stageStaging!, 0);
            }
        }
        finally
        {
            _gpu.Leave();
        }
        return new WriteableBitmapPresent(_stageBits!, width, height);
    }

    void WriteQuad(GpuDraw draw, int destW, int destH, Display? outputDisplay)
    {
        var (x, y, w, h) = GpuLayerMath.FitDrawToDest(draw.X, draw.Y, draw.W, draw.H, destW, destH);
        var x0 = x / destW * 2 - 1;
        var x1 = (x + w) / destW * 2 - 1;
        var y0 = 1 - y / destH * 2;
        var y1 = 1 - (y + h) / destH * 2;
        if (outputDisplay is { Blend: true })
        {
            // Edge blend is applied in the pixel shader via Edge.
        }
        var mapped = _gpu.Context.Map(_vb, 0, MapMode.WriteDiscard);
        try
        {
            var v = new Vert[]
            {
                new() { Pos = new Vector2(x0, y0), Uv = new Vector2(draw.U0, draw.V0) },
                new() { Pos = new Vector2(x1, y0), Uv = new Vector2(draw.U1, draw.V0) },
                new() { Pos = new Vector2(x0, y1), Uv = new Vector2(draw.U0, draw.V1) },
                new() { Pos = new Vector2(x1, y0), Uv = new Vector2(draw.U1, draw.V0) },
                new() { Pos = new Vector2(x1, y1), Uv = new Vector2(draw.U1, draw.V1) },
                new() { Pos = new Vector2(x0, y1), Uv = new Vector2(draw.U0, draw.V1) },
            };
            Marshal.StructureToPtr(v[0], mapped.DataPointer, false);
            var size = Marshal.SizeOf<Vert>();
            for (var i = 0; i < 6; i++)
                Marshal.StructureToPtr(v[i], mapped.DataPointer + i * size, false);
        }
        finally
        {
            _gpu.Context.Unmap(_vb, 0);
        }
    }

    void WriteCbuffer(GpuDraw draw, float edge)
    {
        var m = GpuLayerMath.ColorMatrix(draw);
        var cb = new LayerCbuffer
        {
            Color = new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]),
            ChromaKey = new Color4(draw.ChromaR, draw.ChromaG, draw.ChromaB, draw.ChromaTolerance),
            Wipe = new Color4(draw.Wipe, draw.WipeAngle, draw.WipeFeather, 0),
            Opacity = draw.Opacity,
            ChromaOn = draw.Chroma ? 1 : 0,
            Edge = edge,
            HapQ = _hapQ.TryGetValue(draw.SourceKey, out var hapQ) && hapQ ? 1 : 0,
        };
        var mapped = _gpu.Context.Map(_cb, 0, MapMode.WriteDiscard);
        try { Marshal.StructureToPtr(cb, mapped.DataPointer, false); }
        finally { _gpu.Context.Unmap(_cb, 0); }
        _gpu.Context.VSSetConstantBuffer(0, _cb);
        _gpu.Context.PSSetConstantBuffer(0, _cb);
    }

    public void Dispose()
    {
        foreach (var srv in _srvs.Values) srv.Dispose();
        foreach (var tex in _textures.Values) tex.Dispose();
        _srvs.Clear();
        _textures.Clear();
        _stageRtv?.Dispose();
        _stageTex?.Dispose();
        _stageStaging?.Dispose();
        foreach (var b in _blend) b.Dispose();
        _depth.Dispose();
        _raster.Dispose();
        _sampler.Dispose();
        _cb.Dispose();
        _vb.Dispose();
        _layout.Dispose();
        _ps.Dispose();
        _vs.Dispose();
    }
}

readonly record struct WriteableBitmapPresent(byte[] Pixels, int Width, int Height);
