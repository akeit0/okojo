# Okojo Art Sandbox

Small p5.js-like sketch sandbox for Okojo using SkiaSharp on an OpenGL-backed `SKGLControl`.

Current JS API:

- `createCanvas(width, height)`
- `background(color)` / `background(gray)` / `background(gray, alpha)` / `background(r, g, b)` /
  `background(r, g, b, a)` where `color` accepts CSS-style names like `"orange"` / `"red"` and hex-like strings
- `clear()`
- `fill(color)` / `fill(gray)` / `fill(gray, alpha)` / `fill(r, g, b)` / `fill(r, g, b, a)` where `color` accepts
  CSS-style names like `"orange"` / `"red"` and hex-like strings
- `noFill()`
- `stroke(color)` / `stroke(gray)` / `stroke(gray, alpha)` / `stroke(r, g, b)` / `stroke(r, g, b, a)` where `color`
  accepts CSS-style names like `"orange"` / `"red"` and hex-like strings
- `noStroke()`
- `strokeWeight(width)`
- `circle(x, y, diameter)`
- `rect(x, y, width, height)`
- `line(x1, y1, x2, y2)`
- `random(max)` / `random(min, max)`
- globals: `width`, `height`, `frameCount`

Sketch contract:

- optional `setup()`
- optional `draw()`

Controls:

- `F5` or `Ctrl+R` reloads the current script

Run:

```powershell
dotnet run --project examples\OkojoArtSandbox\OkojoArtSandbox.csproj
```

Run a custom sketch:

```powershell
dotnet run --project examples\OkojoArtSandbox\OkojoArtSandbox.csproj -- path\\to\\sketch.js
```
