<#
.SYNOPSIS
    Checks that Md2OneNote is fully installed (-Installed) or fully removed (-Removed).

.DESCRIPTION
    The installed check reads every registry key the installer and tools\register.ps1 write and
    the files under %LOCALAPPDATA%\Md2OneNote\bin, and fails on the first thing that is wrong.
    The removed check asserts that none of those keys exist and the bin folder is gone — the
    NFR-14 property, in a form that can run on a clean machine or in Windows Sandbox.

.EXAMPLE
    .\tools\verify-install.ps1 -Installed
    .\tools\verify-install.ps1 -Removed
#>
[CmdletBinding(DefaultParameterSetName = 'Installed')]
param(
    [Parameter(ParameterSetName = 'Installed')] [switch] $Installed,
    [Parameter(ParameterSetName = 'Removed')]   [switch] $Removed
)

$ErrorActionPreference = 'Stop'

$clsId   = '{04185F61-8636-4BF0-BCB3-941EE717E8C8}'
$progId  = 'Md2OneNote.AddIn'
$root    = Join-Path $env:LOCALAPPDATA 'Md2OneNote'
$bin     = Join-Path $root 'bin'
$dll     = Join-Path $bin 'Md2OneNote.AddIn.dll'

$keys = @{
    ClsId   = "HKCU:\Software\Classes\CLSID\$clsId"
    AppId   = "HKCU:\Software\Classes\AppID\$clsId"
    ProgId  = "HKCU:\Software\Classes\$progId"
    AddIn   = "HKCU:\Software\Microsoft\Office\OneNote\AddIns\$progId"
}

# CLSID is a redirected key: on 64-bit Windows the installer writes it in both views, so a
# 32-bit OneNote finds it too. This script runs 64-bit and sees the 64-bit view at the plain
# path and the 32-bit view under WOW6432Node.
$clsId32 = "HKCU:\Software\Classes\WOW6432Node\CLSID\$clsId"
if ([Environment]::Is64BitOperatingSystem) { $keys['ClsId32'] = $clsId32 }

$failures = New-Object System.Collections.Generic.List[string]
function Check([bool] $condition, [string] $what) {
    if ($condition) { Write-Host "  ok   $what" } else { Write-Host "  FAIL $what" -ForegroundColor Red; $failures.Add($what) }
}
function Value([string] $key, [string] $name) {
    try { (Get-ItemProperty -Path $key -Name $name -ErrorAction Stop).$name } catch { $null }
}

if ($Removed) {
    Write-Host 'Expecting nothing of Md2OneNote to remain:' -ForegroundColor Cyan
    foreach ($k in $keys.GetEnumerator()) { Check (-not (Test-Path $k.Value)) "no key $($k.Value)" }
    Check (-not (Test-Path $bin)) "no folder $bin"
    $uninstall = Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' -ErrorAction SilentlyContinue |
        Where-Object { (Get-ItemProperty $_.PSPath).DisplayName -like 'Md2OneNote*' }
    Check (-not $uninstall) 'no Apps & features entry'
}
else {
    Write-Host 'Expecting a complete installation:' -ForegroundColor Cyan
    foreach ($k in $keys.GetEnumerator()) { Check (Test-Path $k.Value) "key $($k.Value)" }

    Check ((Value $keys.ClsId '(default)') -eq 'Md2OneNote.AddIn.Connect') 'CLSID default = class name'
    Check ((Value $keys.ClsId 'AppID') -eq $clsId) 'CLSID AppID = CLSID'
    Check ((Value $keys.AppId 'DllSurrogate') -eq '') 'AppID DllSurrogate is empty (system surrogate)'

    $inproc = "$($keys.ClsId)\InprocServer32"
    Check ((Value $inproc '(default)') -eq 'mscoree.dll') 'InprocServer32 = mscoree.dll'
    Check ((Value $inproc 'ThreadingModel') -eq 'Both') 'ThreadingModel = Both'
    Check ((Value $inproc 'Class') -eq 'Md2OneNote.AddIn.Connect') 'Class'
    Check ((Value $inproc 'RuntimeVersion') -eq 'v4.0.30319') 'RuntimeVersion'
    Check ((Value $inproc 'CodeBase') -ieq $dll) "CodeBase = $dll"

    if (Test-Path $dll) {
        $name = [Reflection.AssemblyName]::GetAssemblyName($dll)
        Check ((Value $inproc 'Assembly') -eq $name.FullName) "Assembly = $($name.FullName)"
        Check (Test-Path "$inproc\$($name.Version)") "versioned InprocServer32\$($name.Version)"
        Check ((Value "$inproc\$($name.Version)" 'CodeBase') -ieq $dll) 'versioned CodeBase'
        $info = (Get-Item $dll).VersionInfo
        Write-Host "  info $($info.ProductName) $($info.ProductVersion) by $($info.CompanyName)"
    }
    else {
        Check $false "file $dll"
    }

    Check ((Value "$($keys.ClsId)\ProgId" '(default)') -eq $progId) 'ProgId subkey'
    Check ((Value "$($keys.ClsId)\VersionIndependentProgID" '(default)') -eq $progId) 'VersionIndependentProgID subkey'
    Check (Test-Path "$($keys.ClsId)\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}") 'managed category'
    Check (Test-Path "$($keys.ClsId)\Programmable") 'Programmable'
    Check ((Value "$($keys.ProgId)\CLSID" '(default)') -eq $clsId) 'ProgId -> CLSID'
    Check ((Value $keys.AddIn 'LoadBehavior') -eq 3) 'LoadBehavior = 3'
    Check ((Value $keys.AddIn 'FriendlyName') -eq 'Markdown to OneNote') 'FriendlyName'

    foreach ($file in 'Md2OneNote.Core.dll', 'Md2OneNote.Application.dll', 'Md2OneNote.Interop.dll',
                      'Md2OneNote.Storage.dll', 'Md2OneNote.Diagrams.dll', 'Markdig.dll', 'ColorCode.Core.dll',
                      'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll') {
        Check (Test-Path (Join-Path $bin $file)) "file $file"
    }
    Check ((Get-ChildItem (Join-Path $bin 'runtimes') -Recurse -Filter 'WebView2Loader.dll' -ErrorAction SilentlyContinue).Count -ge 2) 'WebView2Loader.dll under runtimes\'

    $disabled = Get-ChildItem 'HKCU:\Software\Microsoft\Office' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^\d+\.\d+$' } |
        ForEach-Object { "HKCU:\Software\Microsoft\Office\$($_.PSChildName)\OneNote\Resiliency\DisabledItems" } |
        Where-Object { Test-Path $_ } |
        ForEach-Object { $item = Get-Item $_; $item.GetValueNames() | Where-Object {
            ([Text.Encoding]::Unicode.GetString($item.GetValue($_)) -replace "`0", '') -like '*Md2OneNote*' } }
    Check (-not $disabled) 'no DisabledItems entry for the add-in'
}

if ($failures.Count -eq 0) { Write-Host 'All checks passed.' -ForegroundColor Green }
else { Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red; exit 1 }
