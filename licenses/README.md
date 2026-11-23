The artwork and adapted drawing and motion routines derive from the MIT-licensed [Kubai Icons](https://github.com/kubai087/kubai-icons) library. See [the upstream pin](../catalog/upstream.json) for the exact version and commit, and [LICENSE-ICONS](LICENSE-ICONS) for the original notice.

- The Duotone resources come from `icons-duotone/*.json`. Importing compiles paths and static relationships into per-icon KBI binaries while preserving layer and keyframe order.
- The Duotone evaluator, keyline fit, color roles, masks, keyframes, and easing are adapted from `packages/core/src/duotone.js`. The independent motion test fixture records that engine's output.
- The Layered designs come from `icons/*.js`, `badges/*.js` and the composition, palette and motion routines in `packages/core/src`. The importer compiles the base scenes, badge glyphs, and shared slash into native resources; it also generates variant placements.
- The blue Folder in Studio’s sidebar, application icon, and README header is the resting frame of the MIT library’s `icons/folder.js`, rendered with its default Layered palette.
- Studio classifications and tags come from the pinned `kubai-icons` package's `dist/icons.json` and `dist/duotone/icons.json`; bilingual category names and extended keywords come from `icons/categories.json` and `icons/keywords.json` at the same pinned commit. This data is embedded only in Studio. The Avalonia browser interface and stable mixed-color mapping are independently implemented.

Sylinko's unofficial Avalonia port is licensed under the root MIT license. Both notices are included in the NuGet package.
