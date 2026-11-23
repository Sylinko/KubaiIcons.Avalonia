# Binary Resources (`KBI v1`) & Performance

`KubaiIcons.Avalonia` compiles all icon definitions at build time into `KBI v1`, an internal binary resource format designed for minimal package footprint,
near-zero cold-start cost, and low managed memory allocation.

## Architecture Overview

### 1. Build-Time Compilation (Zero Runtime SVG/JSON/JS)

The importer processes upstream Duotone JSON definitions and Layered drawing functions ahead of time using Avalonia's managed `PathMarkupParser` into a
recording `IGeometryContext`—requiring no `AppBuilder`, display server, Skia backend, or browser at build time.

Compilation performs:

- SVG path command normalization (relative commands, shorthand curves, elliptical arcs, figure closure, and fill rules).
- Lossless per-path coordinate encoding (fixed-point integer packing when bit-identical to `Float64`, otherwise full `Float64`) and per-block path
  deduplication.
- Resolution of named motion tracks to compact zero-based indices, including whole-icon tracks.
- Layer mask membership, cut-path references, keyline fitting, and rounded stroke/gap widths.
- Identification of stroked layers that require contour-length measurement for reveal animations (only 97 of 288 stroked Duotone layers require measurement).
- Precompiled Layered scene expression trees, rounded endpoints, Bookmark star/ribbon states, and shared badge placements.

All non-essential metadata (human labels, categories, search keywords) is stripped from binary artwork. The runtime library carries **zero `System.Text.Json`
dependency** and **never parses SVG markup, JSON, or JavaScript**.

### 2. Deduplicated Package Footprint

Through shared base scenes and reusable disc badges, **3,573 shipped icon variants** (502 Duotone + 3,071 Layered) compile down to **1,488 independent `.kbi`
embedded resources**:

| Resource Group             | Embedded `.kbi` Files |           Uncompressed Size | Serves                                         |
|----------------------------|----------------------:|----------------------------:|------------------------------------------------|
| `Art/Duotone/*.kbi`        |                   502 |      273,972 B (~267.6 KiB) | 502 Duotone icons                              |
| `Art/Scenes/*.kbi`         |                   968 |     4,483,403 B (~4.28 MiB) | 967 Layered base designs + 1 shared slash      |
| `Art/Badges/*.kbi`         |                    18 |        10,958 B (~10.7 KiB) | 2,033 badge combinations (via placement table) |
| **Total embedded artwork** |             **1,488** | **4,768,333 B (~4.55 MiB)** | **3,573 icon variants**                        |

The complete multi-targeted NuGet package (`.NET 8`, `.NET 10`, and `.NET 11`) is **4,615,327 B (~4.40 MiB)**.

### 3. On-Demand Decoding & Shared Native Geometry

Opening `KubaiIconCatalog`, querying `KubaiIconCatalog.All.Count`, configuring control properties, or measuring an unrendered icon decodes **no** `.kbi` blocks.

Only on an icon's first `Render()` call does the runtime:

1. Open the specific embedded `.kbi` stream for the requested block (`ManifestResourceStream`).
2. Decode compact binary tables and replay precompiled path commands directly into Avalonia's `StreamGeometryContext`.
3. Cache the resulting native `StreamGeometry` (and widened strokes/clips) on the Avalonia UI thread for all instances of that icon or shared base/badge.

Backend-dependent objects (native `StreamGeometry`, widened strokes, clip boolean geometry, and contour-length measurements) are created once using the
consumer's live Avalonia rendering backend—avoiding serialized backend tessellations or raster frames. Morphing Layered paths interpolate vertices at runtime
using the same rounded-path emitter arithmetic used during compilation. Colors, palettes, transforms, playback progress, and interruption poses remain
lightweight per-instance state.

---

## Measured Performance (502 Duotone Icons)

Benchmarked locally on Windows x64, **.NET 8.0.34**, and **Avalonia/Skia 12.1.6**, loading all **502 Duotone blocks** from memory-backed streams with the
artwork cache bypassed and tiered compilation disabled on both builds. Each stage runs 3 warmups followed by 21 measured iterations; reported values are medians
(excluding process startup, disk I/O, drawing, and layout; allocations reflect managed heap bytes only).

| Stage                                     | JSON Baseline |    `KBI v1` | Delta                     |
|-------------------------------------------|--------------:|------------:|---------------------------|
| Resource size (502 icons)                 |     605,898 B |   273,972 B | **−54.8%**                |
| Decode data                               |      10.06 ms |     0.56 ms | **−94.4% (~18× faster)**  |
| Prepare native geometry from decoded data |      15.50 ms |     5.52 ms | **−64.4% (~2.8× faster)** |
| Decode + native geometry preparation      |      28.08 ms |     5.92 ms | **−78.9% (~4.7× faster)** |
| Managed allocation (decode + geometry)    |  12,300,768 B | 3,193,856 B | **−74.0%**                |

> Because real applications only render a subset of the catalog, actual cold-start decode time and managed memory usage are a small fraction of the full-catalog
> figures above.

---

## `KBI v1` Format Specification

`KBI` is an internal resource contract, not a public interchange format. All fixed-width integers are little-endian. Counts use non-negative .NET 7-bit encoded
integers; table references are zero-based (with optional references encoded as `index + 1`, where `0` means absent). Scalar animation and geometry values use
`Float64`, and colors use ARGB `UInt32`.

### Header

Every block starts with a 4-byte magic/version header `4B 42 49 01` (`"KBI"` + version `1`), followed by a 1-byte block kind:

- `0`: Duotone icon
- `1–4`: reserved / retired prototype formats
- `5`: shared Layered disc badge
- `6`: native Layered scene

### Path Commands

Each length-prefixed path begins with a 1-byte coordinate encoding flag and a figure count:

- `0..6`: decimal fixed-point precision (`10^scale`), encoded as ZigZag + 7-bit signed integer. Selected by the compiler **only** when every decoded coordinate
  round-trips to the exact `Float64` bit pattern of the parser output.
- `255`: raw `Float64` coordinates (retained for negative zero `-0.0` and high-precision values).

Commands follow sequentially:

| Opcode | Command     | Payload                                                                        |
|-------:|-------------|--------------------------------------------------------------------------------|
|    `0` | `FillRule`  | `rule` byte (`EvenOdd = 0`, `NonZero = 1`)                                     |
|    `1` | `Begin`     | start point, `isFilled` byte (`0`/`1`)                                         |
|    `2` | `Line`      | end point, `isStroked` byte (`0`/`1`)                                          |
|    `3` | `Quadratic` | control point, end point, `isStroked` byte                                     |
|    `4` | `Cubic`     | two control points, end point, `isStroked` byte                                |
|    `5` | `Arc`       | end point, two radii, rotation, `isLarge` byte, `sweep` byte, `isStroked` byte |
|    `6` | `End`       | `isClosed` byte (`0`/`1`)                                                      |

The decoder validates figure state transitions, opcode/flag ranges, count bounds against remaining stream length, and finite coordinates before allocating
buffers.

### Duotone Block (Kind `0`)

1. Effective keyline fit and duration.
2. Deduplicated path table.
3. Track table: origin, 8-bit rest-override mask + values, and 8-bit keyframe mask + channel arrays (`x, y, r, s, sx, sy, a, w`; default rest
   `0, 0, 0, 1, 1, 1, 1, 1`).
4. Optional whole-icon track index.
5. Layer table: flags (`light`, `gap`, `needsLength`), path and optional clip indices, effective stroke/gap widths, optional track index, mask-layer indices,
   and cut-path/width pairs.

### Native Layered Scene (Kind `6`)

Stores an expression table, paint table, geometry table, and ordered layer tree preserving nested transforms, local clips, fill rules, gradient stops, stroke
reveals, and drawing order. Non-separable travel is represented via start-pose and elapsed-time curves, while growing polylines pad with degenerate segments so
vertex counts stay continuous. Static shapes and endpoints compile directly into vector commands; soft shadows and inner highlights use lightweight native
vector approximations rather than per-icon offscreen filter surfaces.

### Shared Layered Disc Badge (Kind `5`)

Stores semantic badge color, per-channel pose/entry-rattle curves, compiled paths, and ordered drawing operations with inherited transform chains. Placements in
`CompositionData.g.cs` pair a base icon (`KubaiIconKind`), a shared badge block, and an anchor coordinate without duplicating either block. During playback,
returning from an engaged pose fades active entry motion rather than replaying an entry rattle.
