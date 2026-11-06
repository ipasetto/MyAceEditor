using MyAceEditor.PerfectWorld;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MyAceEditor.UI;

/// <summary>
/// Água como a do jogo (A3DTerrainWaterRender): reflexo do cenário numa textura, relevo com os normal maps animados do cliente
/// (textures\water\waternormal), fresnel e brilho do sol. Sem GLSL ou sem as texturas, Ready fica falso e a água simples segue.
/// </summary>
internal sealed class WorldWater : IDisposable
{
    public const int Size = 768;
    const string Vs = """
        #version 120
        varying vec3 vPos; varying vec4 vClip; varying vec4 vCol; varying float vDepth;
        void main() {
            vPos = gl_Vertex.xyz;
            vec4 eye = gl_ModelViewMatrix * gl_Vertex;
            vDepth = -eye.z;
            gl_Position = gl_ProjectionMatrix * eye;
            vClip = gl_Position; vCol = gl_Color;
        }
        """;
    const string Fs = """
        #version 120
        uniform sampler2D uA, uB, uRef;
        uniform vec3 uCam, uSun, uSunColor, uSky;
        uniform float uTime, uBlend, uHasRef, uFog;
        varying vec3 vPos; varying vec4 vClip; varying vec4 vCol; varying float vDepth;
        vec3 bump(vec2 uv) { return mix(texture2D(uA, uv).xyz, texture2D(uB, uv).xyz, uBlend) * 2.0 - 1.0; }
        void main() {
            vec2 w = vec2(vPos.x, -vPos.z);
            vec3 n1 = bump(w / 26.0 + vec2(uTime * 0.012, uTime * 0.007));
            vec3 n2 = bump(w / 71.0 - vec2(uTime * 0.005, -uTime * 0.008));
            float dist = length(uCam - vPos);
            float calm = 1.0 / (1.0 + dist / 260.0);          // longe a onda vira textura lisa, como no jogo
            vec2 slope = (n1.xy + n2.xy * 0.7) * (0.20 * calm + 0.03);
            vec3 N = normalize(vec3(slope.x, 1.0, slope.y));
            vec3 V = normalize(uCam - vPos);
            float f = pow(1.0 - max(dot(V, N), 0.0), 3.0);
            f = clamp(0.06 + 0.94 * f, 0.0, 1.0);
            vec2 p = vClip.xy / vClip.w * 0.5 + 0.5;
            vec3 refl = uSky;
            if (uHasRef > 0.5) refl = texture2D(uRef, clamp(p + slope * 0.32, 0.002, 0.998)).rgb;
            vec3 col = mix(vCol.rgb, refl, f);
            vec3 H = normalize(V + uSun);
            col += uSunColor * pow(max(dot(N, H), 0.0), 220.0) * 0.9;
            float a = mix(vCol.a, 1.0, f * 0.85);
            if (uFog > 0.5) col = mix(gl_Fog.color.rgb, col, clamp((gl_Fog.end - vDepth) * gl_Fog.scale, 0.0, 1.0));
            gl_FragColor = vec4(col, a);
        }
        """;

    readonly List<int> _frames = [];
    int _program, _fbo, _color, _depth;
    bool _tried;

    public bool Ready => _program != 0 && _frames.Count > 0;
    public bool HasReflection => _fbo != 0;

    public void Init(string texturesPck)
    {
        if (_tried) return;
        _tried = true;
        try
        {
            if (!File.Exists(texturesPck)) return;
            using var a = new PckArchive(texturesPck);
            for (int i = 0; i < 32; i++)
            {
                if (a.Find($@"textures\water\waternormal\water_normal{i:00}.dds") is not { } e || DdsDecoder.DecodeMax(a.Read(e), 256) is not { } img) break;
                _frames.Add(Upload(img.Pixels, img.Width, img.Height));
            }
            if (_frames.Count == 0) return;
            _program = Build();
            if (_program != 0) MakeTarget();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { AppLog.Error("água", ex); }
    }

    static int Upload(int[] pixels, int w, int h)
    {
        int id = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, id);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, w, h, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        return id;
    }

    static int Build()
    {
        int Part(ShaderType type, string src)
        {
            int s = GL.CreateShader(type);
            GL.ShaderSource(s, src); GL.CompileShader(s);
            GL.GetShader(s, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0) { AppLog.Info("água GLSL: " + GL.GetShaderInfoLog(s)); return 0; }
            return s;
        }
        int v = Part(ShaderType.VertexShader, Vs), f = Part(ShaderType.FragmentShader, Fs);
        if (v == 0 || f == 0) return 0;
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f); GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0) { AppLog.Info("água GLSL: " + GL.GetProgramInfoLog(p)); return 0; }
        return p;
    }

    void MakeTarget()
    {
        _color = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _color);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Size, Size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _depth = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, Size, Size);
        _fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _color, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depth);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete) { GL.DeleteFramebuffer(_fbo); _fbo = 0; }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void BeginReflection()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        GL.Viewport(0, 0, Size, Size);
    }

    public static void EndReflection(int width, int height)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, width, height);
    }

    /// <summary>Liga o sombreador; o desenho das áreas de água vem depois, e Stop desliga.</summary>
    public void Start(Vector3 cam, Vector3 sky, Vector3 sunColor, float seconds, bool fog, bool reflect)
    {
        float frame = seconds * 10 % _frames.Count;
        int a = (int)frame, b = (a + 1) % _frames.Count;
        GL.UseProgram(_program);
        GL.ActiveTexture(TextureUnit.Texture2); GL.BindTexture(TextureTarget.Texture2D, _color);
        GL.ActiveTexture(TextureUnit.Texture1); GL.BindTexture(TextureTarget.Texture2D, _frames[b]);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, _frames[a]);
        int U(string n) => GL.GetUniformLocation(_program, n);
        GL.Uniform1(U("uA"), 0); GL.Uniform1(U("uB"), 1); GL.Uniform1(U("uRef"), 2);
        GL.Uniform3(U("uCam"), cam); GL.Uniform3(U("uSky"), sky); GL.Uniform3(U("uSunColor"), sunColor);
        GL.Uniform3(U("uSun"), new Vector3(0.45f, 0.75f, 0.48f).Normalized());
        GL.Uniform1(U("uTime"), seconds); GL.Uniform1(U("uBlend"), frame - a);
        GL.Uniform1(U("uHasRef"), reflect ? 1f : 0f); GL.Uniform1(U("uFog"), fog ? 1f : 0f);
    }

    public static void Stop()
    {
        GL.UseProgram(0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    public void Dispose()
    {
        foreach (int t in _frames) GL.DeleteTexture(t);
        _frames.Clear();
        if (_program != 0) GL.DeleteProgram(_program);
        if (_fbo != 0) GL.DeleteFramebuffer(_fbo);
        if (_color != 0) GL.DeleteTexture(_color);
        if (_depth != 0) GL.DeleteRenderbuffer(_depth);
        _program = _fbo = _color = _depth = 0;
    }
}
