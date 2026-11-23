The artwork and adapted drawing and motion routines derive from the MIT-covered icon library at [kubai-icons@0.14.0](https://github.com/kubai087/kubai-icons/tree/kubai-icons%400.14.0), commit `c0247f253af0d03a6b025a7761fa77ccb43d7d57`. See LICENSE-ICONS for the original notice.

- The 502 Duotone resources come from `icons-duotone/*.json`. Importing compiles paths and static relationships into per-icon KBI binaries while preserving layer and keyframe order.
- The Duotone evaluator, keyline fit, color roles, masks, keyframes, and easing are adapted from `packages/core/src/duotone.js`. The independent motion test fixture records that engine's output.
- The 3,071 Layered designs come from `icons/*.js`, `badges/*.js` and the composition, palette and motion routines in `packages/core/src`. The importer compiles the 967 bases, 18 badge glyphs and shared slash into native resources; it also generates variant placements.
- The blue Folder in Studio’s sidebar, application icon, and README header is the resting frame of the MIT library’s `icons/folder.js`, rendered with its default Layered palette.
- Studio classifications and tags come from the MIT `kubai-icons@0.14.0` package's `dist/icons.json` and `dist/duotone/icons.json`; bilingual category names and extended keywords come from `icons/categories.json` and `icons/keywords.json` at the same pinned commit. This data is embedded only in Studio. The Avalonia browser interface and stable mixed-color mapping are independently implemented.

Sylinko's unofficial Avalonia port is licensed under the root MIT license. Both notices are included in the NuGet package.
