param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$project = 'src/KubaiIcons.Avalonia/KubaiIcons.Avalonia.csproj'
$pin = Get-Content catalog/upstream.json -Raw | ConvertFrom-Json
$version = (dotnet msbuild $project -nologo -getProperty:PackageVersion).Trim()
if ($version -cnotmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$' -or
    $Tag -cne "v$version" -or $pin.version -cne "kubai-icons@$version") {
    throw "Version mismatch: tag=$Tag, package=$version, upstream=$($pin.version)."
}

$out = 'artifacts/release'
if (Test-Path "$out/*nupkg") { throw "Use an empty release output directory: $out" }
$env:PackageReleaseNotes = $env:RELEASE_URL
dotnet pack $project -c Release -o $out -p:ContinuousIntegrationBuild=true -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg

$id = 'KubaiIcons.Avalonia'
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "$out/$id.$version.nupkg"))
try {
    $entries = $archive.Entries.FullName
    $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
    try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($spec.package.metadata.id -cne $id -or $spec.package.metadata.version -cne $version) {
        throw 'Unexpected package identity.'
    }
    foreach ($file in @('LICENSE', 'licenses/LICENSE-ICONS', 'README.md',
        "lib/net8.0/$id.dll", "lib/net10.0/$id.dll", "lib/net11.0/$id.dll")) {
        if ($entries -cnotcontains $file) { throw "Missing package entry: $file" }
    }
    if ($entries -match '(?i)(studio|catalog\.json|\.kbi$)' -or
        @($spec.package.metadata.dependencies.group.dependency | Where-Object { $_.id -cne 'Avalonia' }).Count) {
        throw 'Unexpected Studio content, loose artwork or runtime dependency in the package.'
    }
    if (!(Test-Path "$out/$id.$version.snupkg" -PathType Leaf)) { throw 'Missing symbols package.' }
    Write-Host "Verified $id $version for .NET 8, 10 and 11."
} finally {
    $archive.Dispose()
}
