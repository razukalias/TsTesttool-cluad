<#
.SYNOPSIS
  Builds the tool into one folder with UI, Runner and Instances side by side.

.DESCRIPTION
  publish\
    UI\               the editor (LoadTestingTool.UI.exe)
    Runner\           the test runner (LoadTestingTool.exe)
    Instances\        your instance folders, one per instance (created if missing)
    appsettings.json  the central settings shared by UI and runner (kept if it already exists)
    Start.cmd         starts the UI

  Run it again after every change: UI\ and Runner\ are replaced, while Instances\ and
  appsettings.json are left alone so your data and settings survive.

.EXAMPLE
  .\publish.ps1
  .\publish.ps1 -Output D:\TestTool
  .\publish.ps1 -WithSampleInstances      # also copy the sample instances that are in the repo (only the ones not there yet)
  .\publish.ps1 -SelfContained            # bundle the .NET runtime (bigger, runs without .NET installed)
  .\publish.ps1 -ResetSettings            # overwrite appsettings.json with the repo's version
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot 'publish'),
    [switch]$SelfContained,
    [switch]$WithSampleInstances,
    [switch]$ResetSettings
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$Output = [System.IO.Path]::GetFullPath($Output)

function Publish-Project([string]$project, [string]$target) {
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    $arguments = @('publish', $project, '-c', 'Release', '-o', $target, '--tl:off', '-nologo', '-v', 'quiet')
    if ($SelfContained) { $arguments += @('-r', 'win-x64', '--self-contained', 'true') }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

Write-Host "Output folder: $Output" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host 'Publishing runner...' -ForegroundColor Cyan
Publish-Project (Join-Path $root 'LoadTestingTool.csproj') (Join-Path $Output 'Runner')

Write-Host 'Publishing UI...' -ForegroundColor Cyan
Publish-Project (Join-Path $root 'src\LoadTestingTool.UI\LoadTestingTool.UI.csproj') (Join-Path $Output 'UI')

# Central settings: keep the user's copy unless asked to reset it.
$settings = Join-Path $Output 'appsettings.json'
if ($ResetSettings -or -not (Test-Path $settings)) {
    Copy-Item (Join-Path $root 'appsettings.json') $settings -Force
    Write-Host 'Wrote appsettings.json' -ForegroundColor Cyan
} else {
    Write-Host 'Kept your existing appsettings.json' -ForegroundColor DarkGray
}

# Instances: always exists; samples are copied only when asked, and never over an existing instance.
$instances = Join-Path $Output 'Instances'
New-Item -ItemType Directory -Force -Path $instances | Out-Null
if ($WithSampleInstances) {
    foreach ($source in Get-ChildItem (Join-Path $root 'Instances') -Directory) {
        $destination = Join-Path $instances $source.Name
        if (Test-Path $destination) { Write-Host "Instance $($source.Name) already exists, left as is" -ForegroundColor DarkGray; continue }
        New-Item -ItemType Directory -Path $destination | Out-Null
        Get-ChildItem $source.FullName -File | Where-Object { $_.Extension -in '.xlsx', '.md' -and $_.Name -notlike '~$*' -and $_.Name -notlike '*.backup.*' } |
            Copy-Item -Destination $destination
        foreach ($folder in 'Templates', 'Queries', 'Scripts', 'Inputs', 'Files') {
            $path = Join-Path $source.FullName $folder
            if (Test-Path $path) { Copy-Item $path (Join-Path $destination $folder) -Recurse }
        }
        Write-Host "Copied sample instance $($source.Name)" -ForegroundColor Cyan
    }
}

Set-Content -Path (Join-Path $Output 'Start.cmd') -Encoding ASCII -Value @(
    '@echo off',
    'cd /d "%~dp0"',
    'start "" "UI\LoadTestingTool.UI.exe"'
)

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host "  UI        $Output\UI\LoadTestingTool.UI.exe"
Write-Host "  Runner    $Output\Runner\LoadTestingTool.exe"
Write-Host "  Instances $instances   <- put your instance folders here"
Write-Host "  Settings  $settings"
Write-Host "Start it with $Output\Start.cmd"
