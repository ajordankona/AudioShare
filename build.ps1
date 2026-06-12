[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$Run,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src\AudioShare\AudioShare.csproj'
$publishDir = Join-Path $projectRoot 'publish'

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

Write-Host "Publishing AudioShare ($Configuration)..." -ForegroundColor Cyan

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', 'win-x64',
    '-o', $publishDir,
    '/p:PublishSingleFile=true',
    '/p:IncludeNativeLibrariesForSelfExtract=true'
)

if ($SelfContained) {
    $publishArgs += '/p:SelfContained=true'
    Write-Host "  → Self-contained build (~80 MB, no runtime required)" -ForegroundColor Yellow
} else {
    $publishArgs += '/p:SelfContained=false'
    Write-Host "  → Framework-dependent build (requires .NET 8 Desktop Runtime)" -ForegroundColor Yellow
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$exe = Join-Path $publishDir 'AudioShare.exe'
if (-not (Test-Path $exe)) { throw "Expected exe not found at $exe" }

$size = (Get-Item $exe).Length / 1MB
Write-Host ""
Write-Host "Build OK." -ForegroundColor Green
Write-Host "  Exe:  $exe"
Write-Host "  Size: $('{0:N1}' -f $size) MB"

if ($Run) {
    Write-Host ""
    Write-Host "Launching..." -ForegroundColor Cyan
    Start-Process $exe
}
