[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$Unpacked,
    [switch]$Run,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src\AudioShare\AudioShare.csproj'

if ($Unpacked) {
    $outDir = Join-Path $projectRoot 'release\win-unpacked'
} else {
    $outDir = Join-Path $projectRoot 'publish'
}

if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

Write-Host "Publishing AudioShare ($Configuration)..." -ForegroundColor Cyan

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', $outDir
)

if ($Unpacked) {
    # Unpacked: folder of DLLs alongside AudioShare.exe — like Electron's win-unpacked.
    $publishArgs += '/p:PublishSingleFile=false'
    $publishArgs += '/p:SelfContained=true'
    Write-Host "  → Unpacked folder build (self-contained, portable, no runtime required)" -ForegroundColor Yellow
} else {
    $publishArgs += '/p:PublishSingleFile=true'
    $publishArgs += '/p:IncludeNativeLibrariesForSelfExtract=true'
    if ($SelfContained) {
        $publishArgs += '/p:SelfContained=true'
        Write-Host "  → Self-contained single-file build (~175 MB, no runtime required)" -ForegroundColor Yellow
    } else {
        $publishArgs += '/p:SelfContained=false'
        Write-Host "  → Framework-dependent single-file build (requires .NET 8 Desktop Runtime)" -ForegroundColor Yellow
    }
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$exe = Join-Path $outDir 'AudioShare.exe'
if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }

$exeSize = (Get-Item $exe).Length / 1MB
$totalSize = (Get-ChildItem $outDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
$fileCount = (Get-ChildItem $outDir -Recurse -File).Count

Write-Host ""
Write-Host "Build OK." -ForegroundColor Green
Write-Host "  Out:        $outDir"
Write-Host "  Exe:        $exe ($('{0:N1}' -f $exeSize) MB)"
Write-Host "  Folder:     $fileCount files, $('{0:N1}' -f $totalSize) MB total"

if ($Run) {
    Write-Host ""
    Write-Host "Launching..." -ForegroundColor Cyan
    Start-Process $exe
}
