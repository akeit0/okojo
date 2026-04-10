using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;

namespace OkojoArtSandbox;

internal sealed class SilkSketchApp : IDisposable
{
    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aUv;
        out vec2 vUv;
        void main()
        {
            vUv = aUv;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTexture;
        out vec4 fragColor;
        void main()
        {
            fragColor = texture(uTexture, vUv);
        }
        """;

    private readonly SketchRuntime runtime;

    private readonly string scriptPath;
    private readonly FileSystemWatcher scriptWatcher;
    private readonly IWindow window;
    private SKBitmap? bitmap;
    private SKCanvas? canvas;
    private bool disposed;

    private GL? gl;
    private uint indexBuffer;
    private uint program;
    private bool reloadRequested = true;
    private int surfaceHeight;
    private int surfaceWidth;
    private uint texture;
    private uint vertexArray;
    private uint vertexBuffer;

    public SilkSketchApp(string scriptPath)
    {
        this.scriptPath = scriptPath;
        runtime = new(scriptPath);
        runtime.CanvasSizeRequested += OnCanvasSizeRequested;

        scriptWatcher = new(Path.GetDirectoryName(scriptPath)!, Path.GetFileName(scriptPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
        };
        scriptWatcher.Changed += (_, _) => reloadRequested = true;
        scriptWatcher.Created += (_, _) => reloadRequested = true;
        scriptWatcher.Renamed += (_, _) => reloadRequested = true;
        scriptWatcher.EnableRaisingEvents = true;

        var options = WindowOptions.Default;
        options.Title = "Okojo Art Sandbox";
        options.Size = new(runtime.CanvasWidth, runtime.CanvasHeight);
        options.API = new(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new(3, 3));
        options.IsVisible = true;
        options.ShouldSwapAutomatically = true;
        window = Window.Create(options);
        window.Load += OnLoad;
        window.Render += OnRender;
        window.Resize += OnResize;
        window.Closing += OnClosing;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        scriptWatcher.Dispose();
        bitmap?.Dispose();
        canvas?.Dispose();
        ReleaseGraphicsResources();
        runtime.Dispose();
        window.Dispose();
    }

    public void Run()
    {
        window.Run();
    }

    private void OnLoad()
    {
        gl = GL.GetApi(window);
        program = CreateProgram();
        CreateFullscreenQuad();
        texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        EnsureSurface(window.Size.X, window.Size.Y);
    }

    private unsafe void OnRender(double _)
    {
        if (gl is null || canvas is null || bitmap is null)
            return;

        if (reloadRequested)
        {
            reloadRequested = false;
            runtime.ReloadScript();
            window.Title = $"Okojo Art Sandbox | {Path.GetFileName(scriptPath)}";
        }

        runtime.Render(canvas, surfaceWidth, surfaceHeight);

        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
            return;

        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            (int)InternalFormat.Rgba8,
            (uint)surfaceWidth,
            (uint)surfaceHeight,
            0,
            PixelFormat.Bgra,
            PixelType.UnsignedByte,
            pixmap.GetPixels().ToPointer());

        gl.ClearColor(0f, 0f, 0f, 1f);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        gl.UseProgram(program);
        gl.BindVertexArray(vertexArray);
        gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0);
    }

    private void OnResize(Vector2D<int> size)
    {
        if (gl is null)
            return;

        gl.Viewport(size);
        EnsureSurface(size.X, size.Y);
    }

    private void OnClosing()
    {
        scriptWatcher.EnableRaisingEvents = false;
        ReleaseGraphicsResources();
    }

    private void ReleaseGraphicsResources()
    {
        if (gl is null)
            return;

        if (texture != 0)
        {
            gl.DeleteTexture(texture);
            texture = 0;
        }

        if (program != 0)
        {
            gl.DeleteProgram(program);
            program = 0;
        }

        if (vertexBuffer != 0)
        {
            gl.DeleteBuffer(vertexBuffer);
            vertexBuffer = 0;
        }

        if (indexBuffer != 0)
        {
            gl.DeleteBuffer(indexBuffer);
            indexBuffer = 0;
        }

        if (vertexArray != 0)
        {
            gl.DeleteVertexArray(vertexArray);
            vertexArray = 0;
        }

        gl = null;
    }

    private void OnCanvasSizeRequested(int width, int height)
    {
        window.Size = new(width, height);
    }

    private void EnsureSurface(int width, int height)
    {
        var nextWidth = Math.Max(1, width);
        var nextHeight = Math.Max(1, height);
        if (bitmap is not null && nextWidth == surfaceWidth && nextHeight == surfaceHeight)
            return;

        surfaceWidth = nextWidth;
        surfaceHeight = nextHeight;
        canvas?.Dispose();
        bitmap?.Dispose();
        bitmap = new(new(surfaceWidth, surfaceHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        canvas = new(bitmap);
    }

    private unsafe void CreateFullscreenQuad()
    {
        if (gl is null)
            return;

        float[] vertices =
        [
            -1f, -1f, 0f, 1f,
            1f, -1f, 1f, 1f,
            1f, 1f, 1f, 0f,
            -1f, 1f, 0f, 0f
        ];

        uint[] indices = [0, 1, 2, 2, 3, 0];

        vertexArray = gl.GenVertexArray();
        vertexBuffer = gl.GenBuffer();
        indexBuffer = gl.GenBuffer();

        gl.BindVertexArray(vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, vertexBuffer);
        fixed (float* verticesPtr = vertices)
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), verticesPtr,
                BufferUsageARB.StaticDraw);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, indexBuffer);
        fixed (uint* indicesPtr = indices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), indicesPtr,
                BufferUsageARB.StaticDraw);
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float),
            (void*)(2 * sizeof(float)));
    }

    private uint CreateProgram()
    {
        if (gl is null)
            return 0;

        var vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        var fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        var shaderProgram = gl.CreateProgram();
        gl.AttachShader(shaderProgram, vertexShader);
        gl.AttachShader(shaderProgram, fragmentShader);
        gl.LinkProgram(shaderProgram);
        gl.GetProgram(shaderProgram, ProgramPropertyARB.LinkStatus, out var linkStatus);
        if (linkStatus == 0)
            throw new InvalidOperationException("OpenGL link failed: " + gl.GetProgramInfoLog(shaderProgram));

        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        return shaderProgram;
    }

    private uint CompileShader(ShaderType shaderType, string source)
    {
        if (gl is null)
            return 0;

        var shader = gl.CreateShader(shaderType);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
            throw new InvalidOperationException($"OpenGL {shaderType} compile failed: {gl.GetShaderInfoLog(shader)}");

        return shader;
    }
}
