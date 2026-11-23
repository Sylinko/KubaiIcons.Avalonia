# Importing & Enum Compatibility

`tools/KubaiIcons.Importer` is a headless .NET console tool that compiles pinned upstream icon definitions into `KBI v1` binaries and C# catalog tables. It uses
Avalonia's managed path parser without requiring a GUI, native renderer, browser, or interactive prompts.

## Build & Verify Resources

Build with the **.NET 11 SDK** pinned in `global.json` (running the importer also uses the .NET 8 runtime). Check out the pinned upstream commit from
`catalog/upstream.json`:

```sh
git clone --filter=blob:none --depth 1 --sparse --branch kubai-icons@0.14.0 https://github.com/kubai087/kubai-icons.git artifacts/upstream
git -C artifacts/upstream sparse-checkout set --no-cone /icons-duotone/ /LICENSE-ICONS /icons/ /badges/ /packages/core/src/ /packages/core/package.json /package.json

dotnet run --project tools/KubaiIcons.Importer -- generate --source artifacts/upstream
dotnet run --project tools/KubaiIcons.Importer -- check --source artifacts/upstream
```

Run from the repository root (or pass `--root /path/to/KubaiIcons.Avalonia`). The source directory must be an unmodified Git checkout at the pinned commit.

- `generate` validates all inputs before writing changed outputs (`catalog/ids.props`, `src/KubaiIcons.Avalonia/CatalogData.g.cs`,
  `src/KubaiIcons.Avalonia/Art/Duotone/*.kbi`, and `licenses/LICENSE-ICONS`).
- `check` computes the exact same outputs and reports drift without touching disk.
- **Exit codes**: `0` = success / up to date, `1` = drift detected in `check` mode, `2` = invalid inputs, compatibility violation, or operational error.

Committed generated outputs mean library consumers need neither the importer nor an upstream checkout. `CatalogData.g.cs` emits rows sorted by ordinal C# name
for clean diffs while populating permanent ID slots directly (`O(1)` lookup, zero runtime sorting or dictionary construction). A separate generated switch maps each Duotone kind to its entry duration in milliseconds, so playback can start before artwork is decoded.

## Compile Layered Resources

With **Node.js 24** available (no `npm install`, GUI, or browser required), compile or verify Layered scenes, shared disc badges, placements, and labels from
the pinned upstream checkout (`default: artifacts/upstream`):

```sh
dotnet run --project tools/KubaiIcons.Importer -- layered-generate --source artifacts/upstream
dotnet run --project tools/KubaiIcons.Importer -- layered-check --source artifacts/upstream
```

(`sampled-generate` and `sampled-check` remain aliases.) Unrecognized SVG elements or mask structures fail compilation immediately so the build-time compiler
can be updated and verified before accepting new output.

---

## Enum Stability & Compatibility Rules

Enum stability is enforced at compile/import time and in CI:

- **`catalog/ids.props`** is the permanent identity ledger. Existing entries remain an **unchanged prefix**. Only newly discovered C# names are sorted (ordinal
  comparison) and assigned consecutive integers after the current maximum.
- **Cross-family reuse**: Adding a second style for an existing icon reuses its original ID in place. `KubaiLayeredIconKind` and `KubaiDuotoneIconKind` filter
  the shared ledger in place, declaring only shipped members; `KubaiIconKind` includes all reserved identities.
- **`catalog/enum-baseline.json`** freezes the `0.14.0` baseline (`None = 0` followed by all 3,074 identities). Future imports must preserve this prefix
  verbatim.
- **CI baseline check**: Both locally and in GitHub Actions (against the PR base or previous commit), pass `--baseline`:

```sh
dotnet run --project tools/KubaiIcons.Importer -- check --source artifacts/upstream --baseline artifacts/base-ids.props
```

Duplicate IDs, name collisions, renamed or renumbered members, reordered declarations, removed icons, and unsupported schema fields abort before writing. An
upstream rename or deletion requires an explicit compatibility decision—IDs are never silently recycled.

---

## Deterministic Output

- Artwork binaries strip names, labels, categories, and search tags (Studio search metadata stays strictly separated).
- Paths use lossless numeric encoding rather than coordinate rounding.
- Text outputs use UTF-8 without BOM and LF line endings; binaries follow the fixed `KBI v1` layout with no timestamps.
- CI verifies byte-for-byte determinism across **Windows and Ubuntu**, and tests repeat generation across locales (`tr-TR`, `ar-SA`).

---

## Updating the Upstream Version

**Sync upstream** checks the npm registry daily. It opens one PR for the next stable `kubai-icons` release in version order. Use **Actions → Sync upstream → Run workflow** to start it sooner. If the PR already exists, the workflow leaves it and any maintainer edits alone. Merging does not publish a package.

Enable **Settings → Actions → General → Allow GitHub Actions to create and approve pull requests**. The workflow only creates PRs; it does not approve or merge them. If GitHub asks for approval to run Check on a bot-created PR, approve the run from the PR.

To reproduce a candidate locally, start on a clean branch with the candidate's exact `kubai-icons@X.Y.Z` tag checked out at `artifacts/upstream`:

```powershell
Copy-Item catalog/ids.props artifacts/base-ids.props
node tools/ci/upstream.mjs pin X.Y.Z
./tools/ci/import.ps1 generate -Baseline artifacts/base-ids.props
./tools/ci/import.ps1 check -Baseline artifacts/base-ids.props
dotnet test KubaiIcons.Avalonia.slnx -c Release
```

The pin step verifies npm's `gitHead` against the source commit and extracts the two manifests from that exact package. The importer compiles Layered first, assigns shared IDs second, then generates Studio metadata. The independent test inventory comes from the published manifests; Duotone motion samples come from the upstream engine. Neither first-release ID snapshot is regenerated.

Review the PR's new IDs, style backfills and license changes. Open `index.html` in the Windows Check artifact **upstream-artwork-comparison** to compare upstream and Avalonia at matching poses. A changed shared scene or badge also selects its dependent icons. Update version-specific claims in both READMEs and the NuGet README before release; historical benchmarks keep their original version and sample size.

Unknown motion/schema features or removed public members stop generation before the bot opens a PR. Fix the build-time adapter or make an explicit compatibility decision, then rerun. If an intentional artwork change breaks a manually curated pixel fixture, review the original before updating it.

---

## Generate Studio Metadata Separately

Studio's bilingual categories, Chinese labels, and search keywords live only in `samples/IconStudio/Catalog/catalog.json` and are **not** shipped in the icon
library NuGet package:

```powershell
git -C artifacts/upstream sparse-checkout add /icons/categories.json /icons/keywords.json

node tools/ci/upstream.mjs fetch

dotnet run --project tools/KubaiIcons.Importer -- studio-generate --source artifacts/upstream --metadata artifacts/studio-metadata
dotnet run --project tools/KubaiIcons.Importer -- studio-check --source artifacts/upstream --metadata artifacts/studio-metadata
```

`studio-generate` and `studio-check` validate package version, commit hash, identity coverage, category IDs, duplicate names, and non-empty search tags without
ever modifying `catalog/ids.props`.

---

## GitHub Actions & Visual Regression

### CI Workflow (`.github/workflows/check.yml`)

Runs Duotone, Layered, and Studio checks, builds, tests, and NuGet packing (`artifacts/package`) on **Windows and Ubuntu** with read-only permissions. Manual
`workflow_dispatch` runs support `check` or `generate` (uploading generated outputs as review artifacts without pushing commits or publishing packages).

### Layered Visual Comparison

For full pixel-level comparison against the upstream renderer using a local Chromium executable (`msedge.exe` / `chrome`):

```powershell
node tools/validation/layered-reference.mjs artifacts/upstream artifacts/studio-metadata/layered-icons.json artifacts/layered-validation $browser
$env:KUBAI_REFERENCE_DIR = (Resolve-Path artifacts/layered-validation).Path
dotnet test KubaiIcons.Avalonia.slnx -c Release
```

For 0.14.0 this renders **5,005 poses** through Avalonia (rest, Soft entry, and interrupted return for every base design, plus one moving frame per composition) and
produces contact sheets alongside `errors.tsv` ranked by mean RGB error and foreground IoU.

### Duotone Motion Reference

`tests/KubaiIcons.Tests/Contracts/duotone-motion.json` records five time samples per track across all **502 Duotone** icons directly from upstream
`packages/core/src/duotone.js`, verifying transforms, opacity, stroke reveal, and duration. Set `KUBAI_CAPTURE_DIR` before running tests to export rendered
Duotone contact sheets and Studio screenshots.
