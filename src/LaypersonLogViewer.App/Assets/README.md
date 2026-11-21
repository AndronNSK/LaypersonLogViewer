# App icon

A skinny man in oversized round glasses closely studies the growth rings of a
wooden log. The bold outlines, simple rings, warm wood and teal shirt preserve
the character of the original sketch at small sizes.

- `app-icon.png`: original square RGBA artwork, simplified from the approved
  character sketch using the built-in imagegen tool.
- `app-icon.ico`: Windows executable, application windows, taskbar, installer,
  and shortcuts. Contains 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel frames.
- `app-icon-{size}.png`: transparent exports at 16, 20, 24, 32, 40, 48, 64, 128,
  256, 512, and 1024 pixels. The Debian package installs the standard hicolor
  sizes from 16 through 512 pixels and the desktop entry references them by name.

Regenerate all exports on Windows with `./scripts/Export-AppIcon.ps1` from the
repository root. The exporter resizes the source and packages the ICO; it does
not generate new artwork. The app embeds the ICO as an Avalonia resource and
uses it as the default icon for all application windows.

## Simplification prompt

Reference image: the approved sketch of a skinny man in glasses studying the
growth rings of two wooden logs.

```text
Use case: precise-object-edit
Asset type: simplified small-size desktop app icon concept, based on the attached skinny man studying wooden logs sketch.
Primary request: simplify this exact character concept for legibility at small icon sizes. Preserve the recognizable skinny bespectacled man examining a WOODEN LOG, but redesign with dramatically fewer, bolder shapes.
Input image: reference and edit target, the previous hand-drawn concept of a lanky man in round glasses examining timber growth rings.
Keep: long narrow face and thin neck, oversized ROUND dark glasses, curious thoughtful expression, slightly messy brown hair, muted teal clothing, warm honey-colored cut wood, visual relationship of man leaning close to study the log.
Change: tight compact head-and-shoulder crop. One single short wooden log at lower right, with its large round sawn end facing the viewer. The skinny man's head leans toward it from upper left. His glasses and the log end sit very close together, making one compact recognizable silhouette. Show a small narrow shoulder shape and at most one very simple hand/finger touching the log; omit sprawling arms and the second log.
Style: clean bold flat illustration, icon-ready shapes, thick consistent dark-brown outlines, smooth controlled curves, solid color fills, minimal shading. Keep the eccentric charm of the sketch while removing sketch texture completely.
Small-size design: glasses formed by two very large clean circles with thick rims and simple dark pupils. Face, nose, and hair reduced to a handful of strong shapes; no individual hair strands. Log end uses only TWO thick widely spaced growth rings around a center dot, bark uses just two broad color facets. No wood grain or crosshatching. Strong contrast, optical balance, no fine lines, no tiny details. Designed to remain legible at 32 and 48 pixels.
Composition: square, centered single finished simplified icon concept, artwork occupies about 84 percent of canvas, generous transparent outside margin. No surrounding tile.
Color palette: warm ivory skin, deep brown outlines and hair, muted teal shirt, golden ochre log face, russet bark; at most six main colors.
Constraints: genuine transparent background. Exactly one man, one wooden log, one composition. No computer log text, no paper strip, no magnifier, no background, no words, no letters, no numbers, no watermark, no presentation mockup, no multiple variants. Do not add extra limbs or fingers. Preserve distinctly skinny proportions and large round eyeglasses.
```
