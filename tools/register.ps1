<#
.SYNOPSIS
    Registers or removes the Md2OneNote COM add-in for the current user.

.DESCRIPTION
    Per-user registration only: every key written lives under HKCU, so this never needs
    administrator rights (IMPLEMENTATION.md §10, NFR-10). Nothing is written to HKLM, and
    regasm is not used.

    Uninstall removes every key install created, which is the property NFR-14 asks for and the
    reason each one is listed in a single place below.

.PARAMETER Install
    Register the add-in.

.PARAMETER Uninstall
    Remove the registration.

.PARAMETER Path
    The built Md2OneNote.AddIn.dll. Defaults to the Debug build in this repository.

.EXAMPLE
    .\tools\register.ps1 -Install
    .\tools\register.ps1 -Uninstall
#>
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [switch] $Install,

    [Parameter(ParameterSetName = 'Uninstall')]
    [switch] $Uninstall,

    [string] $Path
)

$ErrorActionPreference = 'Stop'

# Must match the Guid on Md2OneNote.AddIn.Connect.
$ClsId       = '{04185F61-8636-4BF0-BCB3-941EE717E8C8}'
$ProgId      = 'Md2OneNote.AddIn'
$ClassName   = 'Md2OneNote.AddIn.Connect'
$FriendlyName = 'Markdown to OneNote'
$Description = 'Imports Markdown files as styled OneNote pages'

# OneNote's add-in path is unversioned, unlike Word's and Excel's (IMPLEMENTATION.md §10).
$AddInKey  = "HKCU:\Software\Microsoft\Office\OneNote\AddIns\$ProgId"
$ClsIdKey  = "HKCU:\Software\Classes\CLSID\$ClsId"
$ProgIdKey = "HKCU:\Software\Classes\$ProgId"
$AppIdKey  = "HKCU:\Software\Classes\AppID\$ClsId"

<#
    OneNote does not load COM add-ins into ONENOTE.EXE. It activates them as local servers, and a
    DLL can only be a local server through a COM surrogate (dllhost.exe). That surrogate holds the
    add-in DLL open for a while after OneNote exits, so a redeploy has to wait for both.
#>
function Get-Surrogate {
    Get-CimInstance Win32_Process -Filter "Name='dllhost.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.ToUpperInvariant().Contains($ClsId.ToUpperInvariant()) }
}

function Assert-OneNoteClosed {
    $running = Get-Process -Name 'ONENOTE' -ErrorAction SilentlyContinue
    if ($running) {
        throw ("OneNote is running (PID $($running.Id -join ', ')). Close it first — registration " +
               "changes are read at startup, and rewriting them under a live OneNote leaves it " +
               "holding a stale registration.")
    }

    $surrogate = Get-Surrogate
    if ($surrogate) {
        throw ("The add-in's COM surrogate is still alive (dllhost.exe PID " +
               "$($surrogate.ProcessId -join ', ')). It exits a few seconds after OneNote does " +
               "and holds the DLL open until then; wait and retry.")
    }
}

function Set-Key {
    param([string] $Key, [hashtable] $Values)

    if (-not (Test-Path $Key)) { New-Item -Path $Key -Force | Out-Null }
    foreach ($name in $Values.Keys) {
        Set-ItemProperty -Path $Key -Name $name -Value $Values[$name]
    }
}

<#
    Copies the build output to a per-user folder and registers from there.

    Two reasons, both of which bit us before this existed. OneNote holds the add-in DLL open for as
    long as it runs, so registering the build tree directly makes every rebuild fail until OneNote
    is closed. And an add-in that runs out of someone's source tree is not what ships — NFR-10 wants
    a per-user install with no elevation, and %LOCALAPPDATA% is where that lands.
#>
function Copy-Payload {
    param([string] $SourceDll)

    $target = Join-Path $env:LOCALAPPDATA 'Md2OneNote\bin'
    $source = Split-Path $SourceDll -Parent

    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }

    Get-ChildItem $source -File | Where-Object { $_.Extension -in '.dll', '.pdb', '.config' } |
        ForEach-Object { Copy-Item $_.FullName -Destination $target -Force }

    Write-Host "  deployed to $target"
    return (Join-Path $target (Split-Path $SourceDll -Leaf))
}

function Invoke-Install {
    if (-not $Path) {
        $Path = Join-Path $PSScriptRoot '..\src\Md2OneNote.AddIn\bin\Debug\net48\Md2OneNote.AddIn.dll'
    }

    $built = (Resolve-Path $Path -ErrorAction SilentlyContinue).Path
    if (-not $built) { throw "Assembly not found: $Path`nBuild the solution first." }

    $dll = Copy-Payload -SourceDll $built

    # GetAssemblyName reads the manifest without loading the assembly, so the file stays unlocked.
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($dll)
    $version  = $assembly.Version.ToString()
    # A plain path, not a file:/// URI. The CLR accepts either, but the working reference
    # registration on this machine uses a plain path, and matching it removes a variable.
    $codeBase = $dll

    Write-Host "Registering $($assembly.Name) $version" -ForegroundColor Cyan
    Write-Host "  from $dll"

    # mscoree.dll is the shim that loads the CLR and then our class; Class/Assembly/CodeBase tell
    # it which. CodeBase rather than the GAC means no elevation and no install step.
    $server = @{
        '(default)'      = 'mscoree.dll'
        'ThreadingModel' = 'Both'
        'Class'          = $ClassName
        'Assembly'       = $assembly.FullName
        'RuntimeVersion' = 'v4.0.30319'
        'CodeBase'       = $codeBase
    }

    # The two entries that decide whether OneNote can load the add-in at all. OneNote activates
    # add-ins out of process; AppID + an empty DllSurrogate tells COM to host the DLL in the system
    # surrogate (dllhost.exe). Without them activation fails before any add-in code runs, OneNote
    # reports "a runtime error occurred during the loading of the COM Add-in", and sets
    # LoadBehavior=2 — with nothing in our log, because nothing of ours ever ran. The working
    # reference registration on this machine (River.OneMoreAddIn) carries both, per-user included.
    Set-Key -Key $ClsIdKey -Values @{ '(default)' = $ClassName; 'AppID' = $ClsId }
    Set-Key -Key $AppIdKey -Values @{ 'DllSurrogate' = '' }
    Set-Key -Key "$ClsIdKey\InprocServer32" -Values $server
    Set-Key -Key "$ClsIdKey\InprocServer32\$version" -Values $server
    Set-Key -Key "$ClsIdKey\ProgId" -Values @{ '(default)' = $ProgId }
    Set-Key -Key "$ClsIdKey\VersionIndependentProgID" -Values @{ '(default)' = $ProgId }

    # The three keys regasm writes that a hand-rolled registration easily misses. The managed
    # category marks the class as CLR-hosted; Programmable is the long-standing marker for a class
    # meant to be driven by automation. A working reference registration on this machine
    # (River.OneMoreAddIn) carries all of them.
    Set-Key -Key "$ClsIdKey\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}" -Values @{}
    Set-Key -Key "$ClsIdKey\Programmable" -Values @{}

    Set-Key -Key $ProgIdKey -Values @{ '(default)' = $ClassName }
    Set-Key -Key "$ProgIdKey\CLSID" -Values @{ '(default)' = $ClsId }

    Set-Key -Key $AddInKey -Values @{
        'FriendlyName' = $FriendlyName
        'Description'  = $Description
        'LoadBehavior' = 3
    }

    Clear-Resiliency
    Write-Host "Registered. Start OneNote and look for 'Markdown' on the Insert tab." -ForegroundColor Green
    Write-Host "  (the add-in runs in dllhost.exe, not ONENOTE.EXE; attach a debugger there)"
}

<#
    OneNote sets LoadBehavior to 2 and lists the add-in under Resiliency\DisabledItems when it
    throws during startup, after which it never loads again (IMPLEMENTATION.md §13). A disabled
    add-in cannot repair itself, because it never runs — so clearing it belongs here, in the one
    piece of the product that runs outside OneNote. NFR-3 asks for a supported route back that is
    not "edit the registry by hand"; this is the developer-facing half of it.
#>
function Clear-Resiliency {
    $roots = Get-ChildItem 'HKCU:\Software\Microsoft\Office' -ErrorAction SilentlyContinue |
             Where-Object { $_.PSChildName -match '^\d+\.\d+$' }

    foreach ($root in $roots) {
        $key = "HKCU:\Software\Microsoft\Office\$($root.PSChildName)\OneNote\Resiliency\DisabledItems"
        if (-not (Test-Path $key)) { continue }

        $item = Get-Item $key
        foreach ($name in $item.GetValueNames()) {
            $bytes = $item.GetValue($name)
            $text  = [Text.Encoding]::Unicode.GetString($bytes) -replace "`0", ''
            if ($text -like "*Md2OneNote*") {
                Remove-ItemProperty -Path $key -Name $name
                Write-Host "  cleared a DisabledItems entry for this add-in" -ForegroundColor Yellow
            }
        }
    }
}

function Invoke-Uninstall {
    foreach ($key in @($AddInKey, $ClsIdKey, $ProgIdKey, $AppIdKey)) {
        if (Test-Path $key) {
            Remove-Item -Path $key -Recurse -Force
            Write-Host "  removed $key"
        }
    }

    Write-Host 'Unregistered.' -ForegroundColor Green
}

Assert-OneNoteClosed

if ($Uninstall) { Invoke-Uninstall }
else            { Invoke-Install }
