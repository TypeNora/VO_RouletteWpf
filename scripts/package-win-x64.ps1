param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/VoRoulette.Wpf/VoRoulette.Wpf.csproj"
$releasesRoot = Join-Path $repoRoot "artifacts/releases"
$publishTemp = Join-Path $releasesRoot "$Runtime-publish-temp"
$bundleDir = Join-Path $releasesRoot "VO_RouletteWpf-$Runtime"
$zipPath = Join-Path $releasesRoot "VO_RouletteWpf-$Runtime.zip"
$exeName = "VoRoulette.Wpf.exe"

New-Item -ItemType Directory -Path $releasesRoot -Force | Out-Null

if (Test-Path $publishTemp) {
    Remove-Item -Path $publishTemp -Recurse -Force
}
if (Test-Path $bundleDir) {
    Remove-Item -Path $bundleDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishTemp

$exePath = Join-Path $publishTemp $exeName
if (-not (Test-Path $exePath)) {
    throw "Publish succeeded but executable was not found: $exePath"
}

New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null
Copy-Item -Path $exePath -Destination $bundleDir -Force

$assetsPath = Join-Path $publishTemp "assets"
if (Test-Path $assetsPath) {
    Copy-Item -Path $assetsPath -Destination $bundleDir -Recurse -Force
}

Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force

Remove-Item -Path $publishTemp -Recurse -Force
Remove-Item -Path $bundleDir -Recurse -Force

Write-Host "Created release zip: $zipPath"
