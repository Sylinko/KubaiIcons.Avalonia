param(
    [Parameter(Mandatory)][ValidateSet('check', 'generate')][string]$Mode,
    [string]$Baseline
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$source = 'artifacts/upstream'
$metadata = 'artifacts/studio-metadata'
$project = 'tools/KubaiIcons.Importer'
dotnet run --project $project -c Release -- "layered-$Mode" --source $source
$arguments = @($Mode, '--source', $source, '--metadata', $metadata)
if ($Baseline) { $arguments += @('--baseline', $Baseline) }
dotnet run --project $project -c Release --no-build -- @arguments
dotnet run --project $project -c Release --no-build -- "studio-$Mode" --source $source --metadata $metadata
node tools/validation/upstream-contracts.mjs $Mode $source $metadata
