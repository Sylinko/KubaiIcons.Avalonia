# KubaiIcons.Avalonia

**3,000+ Layered** and **500+ Duotone** animated icon variants for Avalonia, drawn natively and loaded on demand.
Supports **.NET 8, .NET 10, and .NET 11** (`Avalonia >= 12.0.0`).

## Why KubaiIcons.Avalonia

- **Precompiled binary format (`KBI v1`) & on-demand loading**: Every icon is precompiled into compact binary path opcodes, animation tracks, and layer trees.
  The runtime parses **no SVG text, no JSON, and no JavaScript**, and has no `System.Text.Json` dependency. Inspecting `KubaiIconCatalog`, binding properties,
  or measuring unrendered icons decodes **zero** artwork—only the first render opens the needed `.kbi` block and caches shared native `StreamGeometry` on the UI
  thread.
- **Shared artwork**: Layered variants reuse base scenes, badges, and slash overlays.
- **Decode performance**: In the `0.14.0` benchmark (502 Duotone icons), `KBI v1` reduced decode time by **94.4%** (10.06 ms → 0.56 ms),
  decode + native geometry prep by **78.9%** (28.08 ms → 5.92 ms), and managed allocations by **74.0%** (12.30 MB → 3.19 MB) compared with JSON.
- **Permanent, `int`-serializable enum IDs**: `KubaiIconKind`, `KubaiLayeredIconKind`, and `KubaiDuotoneIconKind` share a frozen, append-only integer ledger.
  Existing names and values never change or get recycled—future releases only append new IDs after the current maximum, so you can safely persist `(int)Kind` in
  databases, configs, and protocols.

## Quick Start

Declare `xmlns:i="using:KubaiIcons.Avalonia"`; no `<StyleInclude>` or runtime setup is needed:

```xml
<i:KubaiLayeredIcon Kind="Folder" Size="24"/>
<i:KubaiDuotoneIcon Kind="Camera" Size="24"/>
<i:KubaiDuotoneIcon Kind="Loader"
                    IsPlaying="{Binding IsBusy}"
                    LoopMode="Repeat"/>
```

- **Interaction & pose**: Icons stay at rest by default. Bind `IsEngaged` to hold the action pose, or set it from a parent button’s `:pointerover` and
  `:focus-visible` styles.
- **Persistent state & palettes**: Layered icons support `ColorVariant="Color|Soft"`, `IsActive` for persistent states (such as saved `Bookmark` variants), and
  `IsSemanticBadgeEnabled` / `BadgeColor` for badged icons.
- **Playback**: `IsPlaying`, `LoopMode` (`Once` / `Repeat`), `SpeedRatio`, and `RepeatDelay` work in XAML styles, bindings, and C#. `Play()` and `Stop()` share
  the same player without mutating `IsPlaying`; read `IsAnimating` for live motion status.
- **Catalog & conversions**: `KubaiIconCatalog.All` and `KubaiIconCatalog.Get(kind)` expose upstream names and capabilities (`SupportsStyle`,
  `SupportsActiveState`, `SupportsSoftPalette`) without decoding artwork. Use `TryToLayered` / `TryToDuotone` or `KubaiIcon` (`Kind` + `IconStyle`) for dynamic
  family switching.

See the [repository README, Icon Studio demo, and full documentation](https://github.com/Sylinko/KubaiIcons.Avalonia) for detailed usage, benchmarks, and
architecture notes.

## License

An unofficial Avalonia port of [Kubai Icons](https://icons.kubai.design/) by Kubai087, maintained by Sylinko. Both the port and original artwork are MIT
licensed; both notices are included in this package.
