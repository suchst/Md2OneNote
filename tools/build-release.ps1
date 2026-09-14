<#
.SYNOPSIS
    Builds, tests and packages a release of Md2OneNote into .\artifacts.

.DESCRIPTION
    Produces, under artifacts\:
      payload\            the add-in exactly as it is deployed (dll, config, runtimes\, register.ps1)
      Md2OneNote-<ver>.zip the payload for people who install with the script
      SHA256SUMS          checksums of every file in the zip and the zip itself
      version.txt         the version string, for CI to read

    The version comes from Directory.Build.props; nothing here invents one. The installer
    (installer\Md2OneNote.iss) is compiled separately by CI once it exists.

.PARAMETER SkipTests
    Package without running the test suite. CI never passes this; it exists for a local rebuild
    of a payload that was already tested.

.EXAMPLE
    .\tools\build-release.ps1
#>
[CmdletBinding()]
param(
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$root      = Resolve-Path (Join-Path $PSScriptRoot '..')
$solution  = Join-Path $root 'Md2OneNote.sln'
$addIn     = Join-Path $root 'src\Md2OneNote.AddIn\Md2OneNote.AddIn.csproj'
$artifacts = Join-Path $root 'artifacts'
$payload   = Join-Path $artifacts 'payload'

# The version is read from the props file rather than the built assembly so that the zip name
# carries the pre-release suffix (InformationalVersion), which AssemblyVersion cannot.
$props   = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

Write-Host "Md2OneNote $version" -ForegroundColor Cyan

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

$zip = Join-Path $artifacts "Md2OneNote-$version.zip"
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal

$sums = Get-ChildItem $payload -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring($payload.Length + 1).Replace('\', '/')
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$sums += "{0}  {1}" -f (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $zip -Leaf)
Set-Content -Path (Join-Path $artifacts 'version.txt') -Value $version -Encoding ascii -NoNewline

# Samples for the clean-machine check (tools\sandbox\Md2OneNote.wsb maps artifacts\ in).
New-Item -ItemType Directory -Path (Join-Path $artifacts 'samples') -Force | Out-Null
Copy-Item (Join-Path $root 'tests\Md2OneNote.Core.Tests\Samples\*.md') (Join-Path $artifacts 'samples')

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
    $sums += "{0}  {1}" -f (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $setup.Name
    Write-Host "  $($setup.FullName)"
}
else {
    Write-Host 'Inno Setup not found; skipping the installer (winget install JRSoftware.InnoSetup).' -ForegroundColor Yellow
}

Set-Content -Path (Join-Path $artifacts 'SHA256SUMS') -Value $sums -Encoding ascii

Write-Host "Packaged $zip" -ForegroundColor Green
