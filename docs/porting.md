# Porting Architecture

`KubaiIcons.Avalonia` ports Layered and Duotone artwork from [Kubai Icons](https://github.com/kubai087/kubai-icons) to
native Avalonia controls with precompiled binary resources and a shared playback engine. See [Importing & Enum Compatibility](importing.md) for the sync
workflow and [Binary Resources (`KBI v1`)](binary-resources.md) for the compiled format.

## Visual & Motion Fidelity

- **Layered artwork**: Uses the runtime viewBox (`0 -3 31 31` for Folder) rather than padded static exports. Vertex interpolation and corner radii are computed
  before constructing rounded paths, keeping sheets between front/back faces. Compositions like `FolderPlus` share base geometry while animating the disc badge
  and circular knockout together. `Bookmark` isolates its `IsActive` saved state (color + star) from hover motion. SVG Gaussian shadows and soft highlights are
  rendered with lightweight vector approximations rather than allocating per-icon offscreen filter surfaces.
- **Duotone artwork**: Uses the original 16-unit layer coordinate space with the upstream `13/12` fit for square keylines. The renderer supports fills, strokes,
  static clips, moving cutouts, whole-icon transforms, per-layer origins, opacity, and normalized stroke reveals (summing multi-contour stroke lengths first).
  Opaque gaps use native vector clips; fading gaps use alpha masks so underlying shapes fade smoothly.
- **Authored curves & timing**: Every channel preserves its own keyframes and rest overrides. Earlier segments use the authored cubic in/out curve, and the
  final segment uses the entrance overshoot. Duotone entry duration is `480 ms + 110 ms` per additional keyframe (unless overridden per icon), and return is
  `360 ms` starting from the current on-screen pose. Layered samples use `460 ms`.

## Unified Playback Engine

`KubaiIconBase` hosts one shared custom-drawn renderer and playback state machine—without Avalonia `Animation`, runtime SVG DOMs, JavaScript, or per-layer child
controls:

- **Clear state separation**: `IsEngaged` defines the base pose target; `IsActive` defines persistent state; `IsPlaying` / `Play()` / `Stop()` control explicit
  playback. Completion and methods never mutate `IsPlaying`, preserving style setters and MVVM bindings.
- **Cycle phases**: Playback advances through **Outbound → Returning → Waiting** (`RepeatDelay`). `Repeat` loops full authored cycles (not reversed entrances).
  Switching `Repeat` back to `Once` finishes gracefully after the current movement or immediately if waiting.
- **Seamless interruption**: Changing `IsEngaged` or `IsActive` mid-flight retargets from the exact visible pose without jumping. Adjusting `SpeedRatio`
  mid-transition samples elapsed time at the previous rate first, then applies the new rate cleanly; `RepeatDelay` always measures wall-clock time.
- **Zero idle overhead**: Only visible, enabled, non-zero-sized, attached icons advance frames. Repeat gaps use a single `DispatcherTimer` wakeup rather than
  polling. Playback reads Avalonia’s effective visibility without walking the visual tree. Only icons with motion to suspend or playback to resume observe
  visibility changes. Detaching releases that subscription and invalidates queued callbacks.

## Lazy Resource & ID Discipline

1. **Append-only IDs**: New identities append explicit unused integers to `catalog/ids.props`; existing values and declaration order stay frozen. Backfilling a
   second style for an existing icon reuses its existing ID.
2. **Independent lazy blocks**: Art blocks load individually on first render and cache native `StreamGeometry` on the Avalonia UI thread. Opening
   `KubaiIconCatalog`, binding properties, or measuring unrendered icons never touches `.kbi` resources.
3. **Observable lazy decoding**: `KubaiIcons-Avalonia` emits `EventSource` event `BlockDecoded(name, bytes)` once per decoded block in a process (where `bytes`
   is the encoded `.kbi` stream length), verified by headless tests before any window renders.
4. **Backward-compatible renames**: If an upstream icon is ever renamed, the old enum member should be deprecated rather than removed or reassigned.

## Studio Isolation

`samples/IconStudio` uses `TKCode.Arc` (`1.3.0`) controls, dynamic design tokens, and bilingual category/keyword search metadata
(`samples/IconStudio/Catalog/catalog.json`). None of Studio's Arc dependencies, XLIFF localization resources, or search metadata enter the `KubaiIcons.Avalonia`
NuGet package. See [Icon Studio Design Notes](studio.md).
