<#
.SYNOPSIS
    Builds Release, runs the tests, and packs releases\DarkNights_V<ver>.zip.

.DESCRIPTION
    The zip is laid out to extract straight into an SPT folder:

        BepInEx\plugins\DarkNights\DarkNights.Client.dll

    Nothing else. A loose file at the top of an archive meant to be extracted over an SPT
    folder lands in the install root, where it is litter.

    Before building it checks that the two version strings agree -- the csproj <Version>
    and DarkNightsPlugin.PluginVersion. After building it checks the DLL references no
    game assembly: Assembly-CSharp in Managed is the unpatched original until the SPT
    Launcher applies its delta, so a reference to it is a reference to names the running
    game may not have.

    The zip is written through System.IO.Compression with forward-slash entry names.
    Compress-Archive writes backslashes, which extract on Linux as one file with slashes
    in its name.

.PARAMETER SPTPath
    The SPT install to build against -- used for BepInEx and the UnityEngine modules.

.PARAMETER Install
    Also copy the DLL into BepInEx\plugins\DarkNights under SPTPath. Check that EFT is not
    running first: the game holds the DLL open.

.EXAMPLE
    scripts\pack.ps1 -SPTPath C:\HUH
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
#>
[CmdletBinding()]
param(
    [string]$SPTPath = "C:\HUH",
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root 'src\DarkNights.Client'
$testProject = Join-Path $root 'tests\DarkNights.Tests\DarkNights.Tests.csproj'

function Get-Match {
    param([string]$Path, [string]$Pattern)

    $text = Get-Content -Raw -Path (Join-Path $root $Path)
    $found = [regex]::Match($text, $Pattern)
    if (-not $found.Success) { throw "no version found in $Path" }
    return $found.Groups[1].Value
}

# ---------------------------------------------------------------- the version

$versions = [ordered]@{
    'DarkNights.Client.csproj' = Get-Match 'src\DarkNights.Client\DarkNights.Client.csproj' '<Version>([^<]+)</Version>'
    'DarkNightsPlugin.cs'      = Get-Match 'src\DarkNights.Client\DarkNightsPlugin.cs' 'PluginVersion\s*=\s*"([^"]+)"'
}

# Forced to an array: a single string indexes as characters.
$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    $versions.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-26} {1}" -f $_.Key, $_.Value) }
    throw "the version strings disagree"
}

$version = $distinct[0]
Write-Host "Dark Nights $version" -ForegroundColor Cyan

# ------------------------------------------------------------ build and test

dotnet build (Join-Path $projectDir 'DarkNights.Client.csproj') -c Release --nologo -v q "-p:SPTPath=$SPTPath"
if ($LASTEXITCODE -ne 0) { throw "the build failed" }

dotnet test $testProject --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "the tests failed" }

$dll = Join-Path $projectDir 'bin\Release\DarkNights.Client.dll'
if (-not (Test-Path $dll)) { throw "built, but no DLL at $dll" }

# ------------------------------------------------------------ the references

$allowed = @('mscorlib', 'System', 'System.Core', 'BepInEx', '0Harmony', 'UnityEngine', 'UnityEngine.CoreModule')
$references = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($dll).GetReferencedAssemblies() | ForEach-Object { $_.Name }
$unexpected = @($references | Where-Object { $allowed -notcontains $_ })
if ($unexpected.Count -gt 0) {
    throw ("the DLL references assemblies it must not: " + ($unexpected -join ', '))
}
Write-Host ("references: " + ($references -join ', '))

# ------------------------------------------------------------------ the stage

$stage = Join-Path $root 'dist\stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

$pluginDir = Join-Path $stage 'BepInEx\plugins\DarkNights'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $dll $pluginDir

# -------------------------------------------------------------------- the zip

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$releases = Join-Path $root 'releases'
New-Item -ItemType Directory -Force -Path $releases | Out-Null

$zipPath = Join-Path $releases ("DarkNights_V{0}.zip" -f $version)
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($file in Get-ChildItem -Recurse -File -Path $stage) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, 'Optimal') | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "packed $zipPath" -ForegroundColor Green

# -------------------------------------------------------------- the install

if ($Install) {
    if (Get-Process -Name 'EscapeFromTarkov' -ErrorAction SilentlyContinue) {
        throw "EscapeFromTarkov is running and holds the DLL open. Close the game and run again."
    }

    $destination = Join-Path $SPTPath 'BepInEx\plugins\DarkNights'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Copy-Item $dll $destination -Force
    Write-Host "installed to $destination" -ForegroundColor Green
}

Remove-Item -Recurse -Force $stage
