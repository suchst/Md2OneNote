<#
.SYNOPSIS
    Builds, tests and packages a release of Md2OneNote into .\artifacts.

.DESCRIPTION
    Produces, under artifacts\:
      payload\                    the add-in exactly as it is deployed (dll, config, runtimes\, register.ps1)
      Md2OneNote-<ver>.zip        the payload for people who install with the script
      Md2OneNote-<ver>-setup.exe  the installer, when Inno Setup is available
      SHA256SUMS                  checksums of every file in the zip, the zip and the installer
      version.txt                 the version string, for CI to read

    The version comes from Directory.Build.props; nothing here invents one.

    The work is done in three stages so that the release workflow can sign between them
    (docs\RELEASING.md, "Signing"):
      Build      Release build, tests, artifacts\payload, version.txt, samples
      Package    the zip and the installer from whatever artifacts\payload holds by then
      Checksums  SHA256SUMS over the final files
    Without -Stage all three run in order, which is the unsigned local release.

.PARAMETER Stage
    All (default), Build, Package or Checksums. Package and Checksums expect artifacts\payload
    from an earlier Build.

.PARAMETER SkipTests
    Build without running the test suite. CI never passes this; it exists for a local rebuild
    of a payload that was already tested.

.EXAMPLE
    .\tools\build-release.ps1

.EXAMPLE
    .\tools\build-release.ps1 -Stage Build
    # sign artifacts\payload\Md2OneNote.*.dll
    .\tools\build-release.ps1 -Stage Package
    # sign artifacts\Md2OneNote-<ver>-setup.exe
    .\tools\build-release.ps1 -Stage Checksums
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Build', 'Package', 'Checksums')]
    [string] $Stage = 'All',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$root      = Resolve-Path (Join-Path $PSScriptRoot '..')
$solution  = Join-Path $root 'Md2OneNote.sln'
$artifacts = Join-Path $root 'artifacts'
$payload   = Join-Path $artifacts 'payload'

# The version is read from the props file rather than the built assembly so that the zip name
# carries the pre-release suffix (InformationalVersion), which AssemblyVersion cannot.
$props   = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

$zip = Join-Path $artifacts "Md2OneNote-$version.zip"

function Invoke-Build {
    if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
    New-Item -ItemType Directory -Path $payload -Force | Out-Null

    Write-Host 'Building (Release)' -ForegroundColor Cyan
    & dotnet build $solution -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    if (-not $SkipTests) {
        Write-Host 'Testing' -ForegroundColor Cyan
        & dotnet test $solution -c Release --no-build --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    # Same selection as register.ps1's Copy-Payload: the managed assemblies, their configs, and
    # WebView2's native loader under runtimes\.
    $bin = Join-Path $root 'src\Md2OneNote.AddIn\bin\Release\net48'
    Get-ChildItem $bin -File | Where-Object { $_.Extension -in '.dll', '.config' } |
        ForEach-Object { Copy-Item $_.FullName -Destination $payload }
    $runtimes = Join-Path $bin 'runtimes'
    if (Test-Path $runtimes) { Copy-Item $runtimes -Destination $payload -Recurse }

    # The register script installs from the folder it sits in when a built add-in is next to it,
    # so the zip is a complete script-based install.
    Copy-Item (Join-Path $PSScriptRoot 'register.ps1') -Destination $payload
    Copy-Item (Join-Path $root 'LICENSE') -Destination $payload
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $payload
    Copy-Item (Join-Path $root 'src\Md2OneNote.Diagrams\Assets\mermaid.LICENSE') -Destination $payload

    Set-Content -Path (Join-Path $artifacts 'version.txt') -Value $version -Encoding ascii -NoNewline

    # Samples for the clean-machine check (tools\sandbox\Md2OneNote.wsb maps artifacts\ in).
    New-Item -ItemType Directory -Path (Join-Path $artifacts 'samples') -Force | Out-Null
    Copy-Item (Join-Path $root 'tests\Md2OneNote.Core.Tests\Samples\*.md') (Join-Path $artifacts 'samples')

    Write-Host "Payload in $payload" -ForegroundColor Green
}

function Invoke-Package {
    if (-not (Test-Path (Join-Path $payload 'Md2OneNote.AddIn.dll'))) {
        throw "No payload in $payload; run -Stage Build first."
    }

    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Packaged $zip" -ForegroundColor Green

    # The installer, when Inno Setup is available (it is on GitHub's Windows runners). The
    # assembly version is what the registry's versioned InprocServer32 subkey is named after.
    $iscc = @(
        $env:ISCC,
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if ($iscc) {
        $fileVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload 'Md2OneNote.AddIn.dll')).Version.ToString()
        Write-Host "Compiling the installer (assembly $fileVersion)" -ForegroundColor Cyan
        & $iscc /Q "/DVersion=$version" "/DFileVersion=$fileVersion" (Join-Path $root 'installer\Md2OneNote.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
        $setup = Get-ChildItem $artifacts -Filter '*-setup.exe' | Select-Object -First 1
        Write-Host "  $($setup.FullName)"
    }
    else {
        Write-Host 'Inno Setup not found; skipping the installer (winget install JRSoftware.InnoSetup).' -ForegroundColor Yellow
    }
}

function Write-Checksums {
    if (-not (Test-Path $zip)) { throw "No $zip; run -Stage Package first." }

    $sums = Get-ChildItem $payload -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($payload.Length + 1).Replace('\', '/')
        "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
    # The zip and the installer last, as the release page lists them.
    $sums += Get-ChildItem $artifacts -File | Where-Object { $_.Name -like 'Md2OneNote-*.zip' -or $_.Name -like 'Md2OneNote-*-setup.exe' } |
        ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }

    Set-Content -Path (Join-Path $artifacts 'SHA256SUMS') -Value $sums -Encoding ascii
    Write-Host "Checksums in $(Join-Path $artifacts 'SHA256SUMS')" -ForegroundColor Green
}

Write-Host "Md2OneNote $version ($Stage)" -ForegroundColor Cyan

switch ($Stage) {
    'Build'     { Invoke-Build }
    'Package'   { Invoke-Package }
    'Checksums' { Write-Checksums }
    'All'       { Invoke-Build; Invoke-Package; Write-Checksums }
}
