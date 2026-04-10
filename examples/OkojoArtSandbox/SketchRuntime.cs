using System.Reflection;
using Okojo;
using Okojo.Annotations;
using Okojo.DocGenerator.Annotations;
using Okojo.Objects;
using Okojo.Runtime;
using SkiaSharp;

namespace OkojoArtSandbox;

[GenerateJsGlobals]
[DocDeclaration("globals")]
internal sealed partial class SketchRuntime : IDisposable
{
    private static readonly IReadOnlyDictionary<string, SKColor> NamedColors = CreateNamedColors();
    private readonly DrawState drawState = new();
    private readonly SKPaint fillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly Random random = new();
    private readonly JsRealm realm;
    private readonly JsRuntime runtime;

    private readonly string scriptPath;
    private readonly SKPaint strokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private SKCanvas? currentCanvas;
    private JsFunction? drawFunction;

    private JsFunction? setupFunction;
    private bool setupRan;

    public SketchRuntime(string scriptPath)
    {
        this.scriptPath = scriptPath;
        runtime = JsRuntime.CreateBuilder()
            .UseGlobals(InstallGeneratedGlobals)
            .Build();
        realm = runtime.MainRealm;
    }

    public int CanvasWidth { get; private set; } = 960;

    public int CanvasHeight { get; private set; } = 720;

    /// <summary>Current sketch canvas width in pixels.</summary>
    [JsGlobalProperty("width")]
    public int SketchWidth { get; private set; } = 960;

    /// <summary>Current sketch canvas height in pixels.</summary>
    [JsGlobalProperty("height")]
    public int SketchHeight { get; private set; } = 720;

    /// <summary>Number of completed draw() frames.</summary>
    [JsGlobalProperty("frameCount")]
    public int FrameCount { get; private set; }

    public string? LastError { get; private set; }

    public void Dispose()
    {
        fillPaint.Dispose();
        strokePaint.Dispose();
        runtime.Dispose();
    }

    public event Action<int, int>? CanvasSizeRequested;
    public event Action? ScriptReloaded;

    public void ReloadScript()
    {
        LastError = null;
        FrameCount = 0;
        setupRan = false;
        drawFunction = null;
        setupFunction = null;
        ResetStyleState();
        SketchWidth = CanvasWidth;
        SketchHeight = CanvasHeight;

        var source = File.ReadAllText(scriptPath);
        _ = realm.Eval(source);

        setupFunction = TryGetGlobalFunction("setup");
        drawFunction = TryGetGlobalFunction("draw");
        ScriptReloaded?.Invoke();
    }

    public void Render(SKCanvas canvas, int width, int height)
    {
        currentCanvas = canvas;
        try
        {
            SketchWidth = Math.Max(1, width);
            SketchHeight = Math.Max(1, height);

            if (!setupRan)
            {
                setupRan = true;
                InvokeSketchCallback(setupFunction);
            }

            canvas.Clear(SKColors.Black);
            InvokeSketchCallback(drawFunction);
            FrameCount++;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            canvas.Clear(new(28, 0, 0, 255));
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                TextSize = 18
            };
            canvas.DrawText(ex.Message, 16, 32, paint);
        }
        finally
        {
            currentCanvas = null;
        }
    }

    /// <summary>Requests a sketch canvas size in pixels.</summary>
    /// <param name="width">Requested canvas width.</param>
    /// <param name="height">Requested canvas height.</param>
    [JsGlobalFunction("createCanvas")]
    private void CreateCanvas(int width = 960, int height = 720)
    {
        CanvasWidth = Math.Max(1, width);
        CanvasHeight = Math.Max(1, height);
        SketchWidth = CanvasWidth;
        SketchHeight = CanvasHeight;
        CanvasSizeRequested?.Invoke(CanvasWidth, CanvasHeight);
    }

    /// <summary>Clears the canvas with a parsed color string.</summary>
    [JsGlobalFunction("background")]
    private void Background(string color)
    {
        EnsureCanvas();
        currentCanvas!.Clear(ReadColor(color));
    }

    /// <summary>Clears the canvas with a grayscale value.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte gray)
    {
        EnsureCanvas();
        currentCanvas!.Clear(ReadColor(gray));
    }

    /// <summary>Clears the canvas with a grayscale value and alpha channel.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte gray, byte alpha)
    {
        EnsureCanvas();
        currentCanvas!.Clear(ReadColor(gray, alpha));
    }

    /// <summary>Clears the canvas with RGB color channels.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte r, byte g, byte b)
    {
        EnsureCanvas();
        currentCanvas!.Clear(ReadColor(r, g, b));
    }

    /// <summary>Clears the canvas with RGBA color channels.</summary>
    [JsGlobalFunction("background")]
    private void Background(byte r, byte g, byte b, byte a)
    {
        EnsureCanvas();
        currentCanvas!.Clear(ReadColor(r, g, b, a));
    }

    /// <summary>Clears the canvas to transparent black.</summary>
    [JsGlobalFunction("clear")]
    private void Clear()
    {
        EnsureCanvas();
        currentCanvas!.Clear(SKColors.Transparent);
    }

    /// <summary>Sets the fill color from a parsed color string.</summary>
    [JsGlobalFunction("fill")]
    private void Fill(string color)
    {
        drawState.FillEnabled = true;
        drawState.FillColor = ReadColor(color);
    }

    /// <summary>Sets the fill color from a grayscale value.</summary>
    [JsGlobalFunction("fill")]
    private void Fill(byte gray)
    {
        drawState.FillEnabled = true;
        drawState.FillColor = ReadColor(gray);
    }

    /// <summary>Sets the fill color from a grayscale value and alpha channel.</summary>
    [JsGlobalFunction("fill")]
    private void Fill(byte gray, byte alpha)
    {
        drawState.FillEnabled = true;
        drawState.FillColor = ReadColor(gray, alpha);
    }

    /// <summary>Sets the fill color from RGB color channels.</summary>
    [JsGlobalFunction("fill")]
    private void Fill(byte r, byte g, byte b)
    {
        drawState.FillEnabled = true;
        drawState.FillColor = ReadColor(r, g, b);
    }

    /// <summary>Sets the fill color from RGBA color channels.</summary>
    [JsGlobalFunction("fill")]
    private void Fill(byte r, byte g, byte b, byte a)
    {
        drawState.FillEnabled = true;
        drawState.FillColor = ReadColor(r, g, b, a);
    }

    /// <summary>Disables fill for subsequent shapes.</summary>
    [JsGlobalFunction("noFill")]
    private void NoFill()
    {
        drawState.FillEnabled = false;
    }

    /// <summary>Sets the stroke color from a parsed color string.</summary>
    [JsGlobalFunction("stroke")]
    private void Stroke(string color)
    {
        drawState.StrokeEnabled = true;
        drawState.StrokeColor = ReadColor(color);
    }

    /// <summary>Sets the stroke color from a grayscale value.</summary>
    [JsGlobalFunction("stroke")]
    private void Stroke(byte gray)
    {
        drawState.StrokeEnabled = true;
        drawState.StrokeColor = ReadColor(gray);
    }

    /// <summary>Sets the stroke color from a grayscale value and alpha channel.</summary>
    [JsGlobalFunction("stroke")]
    private void Stroke(byte gray, byte alpha)
    {
        drawState.StrokeEnabled = true;
        drawState.StrokeColor = ReadColor(gray, alpha);
    }

    /// <summary>Sets the stroke color from RGB color channels.</summary>
    [JsGlobalFunction("stroke")]
    private void Stroke(byte r, byte g, byte b)
    {
        drawState.StrokeEnabled = true;
        drawState.StrokeColor = ReadColor(r, g, b);
    }

    /// <summary>Sets the stroke color from RGBA color channels.</summary>
    [JsGlobalFunction("stroke")]
    private void Stroke(byte r, byte g, byte b, byte a)
    {
        drawState.StrokeEnabled = true;
        drawState.StrokeColor = ReadColor(r, g, b, a);
    }

    /// <summary>Disables stroke rendering for subsequent shapes.</summary>
    [JsGlobalFunction("noStroke")]
    private void NoStroke()
    {
        drawState.StrokeEnabled = false;
    }

    /// <summary>Sets the stroke width in pixels.</summary>
    /// <param name="width">Requested stroke width.</param>
    [JsGlobalFunction("strokeWeight")]
    private void StrokeWeight(float width)
    {
        drawState.StrokeWidth = Math.Max(0f, width);
    }

    /// <summary>Draws a circle centered at the given point.</summary>
    [JsGlobalFunction("circle")]
    private void Circle(float x, float y, float diameter)
    {
        EnsureCanvas();
        var radius = Math.Max(0f, diameter) * 0.5f;

        if (drawState.FillEnabled)
            currentCanvas!.DrawCircle(x, y, radius, ConfigureFillPaint());
        if (drawState.StrokeEnabled)
            currentCanvas!.DrawCircle(x, y, radius, ConfigureStrokePaint());
    }

    /// <summary>Draws a rectangle at the given position and size.</summary>
    [JsGlobalFunction("rect")]
    private void Rect(float x, float y, float width, float height)
    {
        EnsureCanvas();
        var rect = new SKRect(
            x,
            y,
            x + width,
            y + height);

        if (drawState.FillEnabled)
            currentCanvas!.DrawRect(rect, ConfigureFillPaint());
        if (drawState.StrokeEnabled)
            currentCanvas!.DrawRect(rect, ConfigureStrokePaint());
    }

    /// <summary>Draws a line segment between two points.</summary>
    [JsGlobalFunction("line")]
    private void Line(float x1, float y1, float x2, float y2)
    {
        EnsureCanvas();
        if (drawState.StrokeEnabled)
            currentCanvas!.DrawLine(x1, y1, x2, y2, ConfigureStrokePaint());
    }

    /// <summary>Returns a random floating-point value in the requested range.</summary>
    [JsGlobalFunction("random")]
    private double RandomValue(double a = 1d, double? b = null)
    {
        var min = b is null ? 0d : Math.Min(a, b.Value);
        var max = b is null ? a : Math.Max(a, b.Value);
        if (max <= min)
            return min;

        return min + random.NextDouble() * (max - min);
    }

    private void InvokeSketchCallback(JsFunction? callback)
    {
        if (callback is null)
            return;

        _ = realm.Call(callback, JsValue.Undefined);
    }

    private void ResetStyleState()
    {
        drawState.FillEnabled = true;
        drawState.StrokeEnabled = true;
        drawState.FillColor = new(236, 179, 82, 255);
        drawState.StrokeColor = new(18, 22, 35, 255);
        drawState.StrokeWidth = 1.5f;
    }

    private JsFunction? TryGetGlobalFunction(string name)
    {
        return realm.Global.TryGetValue(name, out var value) &&
               value.TryGetObject(out var obj) &&
               obj is JsFunction fn
            ? fn
            : null;
    }

    private SKPaint ConfigureFillPaint()
    {
        fillPaint.Color = drawState.FillColor;
        return fillPaint;
    }

    private SKPaint ConfigureStrokePaint()
    {
        strokePaint.Color = drawState.StrokeColor;
        strokePaint.StrokeWidth = drawState.StrokeWidth;
        return strokePaint;
    }

    private static SKColor ReadColor(string color)
    {
        if (SKColor.TryParse(color, out var parsed))
            return parsed;
        var normalized = NormalizeColorName(color);
        if (NamedColors.TryGetValue(normalized, out var named))
            return named;
        throw new InvalidOperationException($"Could not parse color string '{color}'.");
    }

    private static SKColor ReadColor(byte gray)
    {
        return new(gray, gray, gray, 255);
    }

    private static SKColor ReadColor(byte gray, byte alpha)
    {
        return new(gray, gray, gray, alpha);
    }

    private static SKColor ReadColor(byte r, byte g, byte b)
    {
        return new(r, g, b, 255);
    }

    private static SKColor ReadColor(byte r, byte g, byte b, byte a)
    {
        return new(r, g, b, a);
    }

    private static IReadOnlyDictionary<string, SKColor> CreateNamedColors()
    {
        var colors = new Dictionary<string, SKColor>(StringComparer.Ordinal);
        foreach (var field in typeof(SKColors).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(SKColor))
                continue;

            if (field.GetValue(null) is not SKColor color)
                continue;

            colors[NormalizeColorName(field.Name)] = color;
        }

        return colors;
    }

    private static string NormalizeColorName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch))
                continue;
            buffer[count++] = char.ToLowerInvariant(ch);
        }

        return new(buffer[..count]);
    }

    private void EnsureCanvas()
    {
        if (currentCanvas is null || !setupRan)
            throw new InvalidOperationException("Drawing API can only be used inside setup() or draw().");
    }

    private sealed class DrawState
    {
        public SKColor FillColor = new(236, 179, 82, 255);
        public bool FillEnabled = true;
        public SKColor StrokeColor = new(18, 22, 35, 255);
        public bool StrokeEnabled = true;
        public float StrokeWidth = 1.5f;
    }
}
