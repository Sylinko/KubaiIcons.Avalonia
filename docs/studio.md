# Icon Studio Design Notes

`samples/IconStudio` is the interactive desktop browser for `KubaiIcons.Avalonia`. It keeps the main workflow short—search or pick a category, preview poses and
palettes, and copy ready-to-paste **AXAML** or **C#**.

## Layout & UX

- **Docked search header & floating inspector**: The gallery scrolls beneath a full-width search bar; selecting a tile opens a solid rounded inspector card at
  the bottom. Only the search header blurs tiles scrolling underneath (`ReduceTransparency` turns it opaque).
- **Direct toolbar controls**: Artwork family (`Layered` / `Duotone`), Layered palette (`Color` / `Soft`), deterministic **Mixed** color wheel (seamless conic
  gradient keyed by upstream icon name), five solid swatches, custom color picker (`+`), and a single-row **Size** slider (`16–64` DIP, updating realized tiles
  and code snippets live without resizing the fixed inspector preview).
- **Inspector & quick copy**: Click the enum member name beneath the title to copy the identifier (e.g., `BinderSync`). Hover the preview to animate, toggle
  **Hold pose** (`IsEngaged`) or **Active** (`IsActive`), trigger **Replay** / **Stop**, or open **Animation settings** (`Animate`, `IsPlaying`, `LoopMode`,
  `SpeedRatio`, `RepeatDelay`).
- **Keyboard navigation**: `/` or `Ctrl/Cmd+K` focuses search; arrow keys move across tiles; `Enter` inspects; `Esc` closes the innermost popover/dropdown
  before dismissing the inspector and restores focus to the trigger.

## `TKCode.Arc` Integration

Icon Studio pins `TKCode.Arc`, `TKCode.Arc.Blocks`, and `TKCode.Arc.Fonts` to **1.3.0** on **Avalonia 12.1.6**, while the core `KubaiIcons.Avalonia` library
keeps its **Avalonia 12.0.0** floor and **zero Arc dependency**.

Following [Arc guidance](https://docs.tkcode.si/skills/tkcode-arc/SKILL.md) and [Arc 1.3.0](https://github.com/AIDotNet/TKCode.Arc/tree/v1.3.0):

- **`ArcWindow`**: The title bar shares the sidebar’s pane fill around the gallery’s rounded upper-left corner. Arc places the brand after the native traffic
  lights on macOS. The GitHub, language, and theme buttons use the caption-button size with a circular hover fill.
- **`Sidebar`, `SearchField`, `SegmentedControl`, `Select`, `Switch`, `Popover`, `ColorPicker`, `EmptyState`, `ThemeSwitcher`**: Drive navigation, filtering,
  settings forms, and light/dark theme switching using dynamic Arc design tokens (`Inter`, `Geist`, `Geist Mono`) and a lightweight `Spacing` attached-property
  bridge from scalar Arc `Space` tokens to Avalonia `Thickness`.
- **`Slider` & `CodeBlock`**: Scoped `PART_Header` styles adapt Arc's header slots to place inline size readouts and keep the file name + `CopyButton` fixed
  above the scrolling code body while preserving accessible names.
- **`Tooltip`**: Uses Arc tooltips exclusively (including `ShowOnDisabled` on unsupported `Active` switches) to keep surface styling consistent.

## Virtualization & Performance

- **Row-virtualized gallery**: An outer `ListBox` with `VirtualizingStackPanel` virtualizes rows of `UniformGrid` tiles sized to the viewport width, keeping
  overlay scrollbars cleanly outside the last column and scroll insets clear of the header and inspector.
- **Lazy artwork & detached players**: Searching evaluates Studio's metadata only (`0` `.kbi` blocks decoded); only visible tiles and their small scroll buffer
  trigger lazy decoding, and closing the inspector detaches its player.

## Bilingual Search & Metadata

- Supports **English** and **简体中文**, defaulting to the OS UI language on first launch and persisting user preference thereafter. Language switching updates
  realized tiles and UI in place without rebuilding search results or re-decoding artwork.
- Metadata in `samples/IconStudio/Catalog/catalog.json` merges upstream categories, tags, Chinese labels, and base keyword groups (shape, object, meaning,
  action, and Chinese synonyms) across all 3,074 identities—staying strictly inside the Studio sample executable.

## Verification

```sh
dotnet test tests/KubaiIcons.Tests
```

Studio UI tests cover virtualized browsing, Chinese/English search (including spaced/unspaced middle dots like `档案夹 · 同步`), capability-gated switches,
popover focus/Escape handling, pointer dragging on the size slider, clipboard output, and theme switching. Set `KUBAI_CAPTURE_DIR` to save rendered Studio
screenshots across themes and states. Dependency notices under `licenses/studio` copy only to Studio build/publish output.
