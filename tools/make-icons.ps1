<#
.SYNOPSIS
    Renders assets\brand\icon.svg into every raster form the product needs.

.DESCRIPTION
    Produces:
      assets\brand\png\icon-<N>.png       N in 16, 20, 24, 32, 48, 64, 128, 256 (transparent)
      assets\brand\icon.ico               16/24/32/48 as classic 32-bit bitmaps, 64/128/256 as PNG
      assets\brand\social-preview.png     1280x640, from assets\brand\social-preview.html
      src\Md2OneNote.AddIn\Images\*.png   the ribbon sizes, embedded into the add-in

    Rendering uses the Microsoft Edge that every supported Windows has, in headless mode, so no
    design tool is needed. The ICO is assembled by hand: the format is a 6-byte header,
    16-byte directory entries, then the images; classic entries are a BITMAPINFOHEADER plus
    bottom-up BGRA rows plus an AND mask, which is what Inno Setup and older shell code expect
    for the small sizes.

.PARAMETER Source
    The SVG to render. Defaults to assets\brand\icon.svg; point it at a variant to compare.

.EXAMPLE
    .\tools\make-icons.ps1
#>
[CmdletBinding()]
param(
    [string] $Source
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root  = Resolve-Path (Join-Path $PSScriptRoot '..')
$brand = Join-Path $root 'assets\brand'
$png   = Join-Path $brand 'png'
$addInImages = Join-Path $root 'src\Md2OneNote.AddIn\Images'
if (-not $Source) { $Source = Join-Path $brand 'icon.svg' }
$Source = (Resolve-Path $Source).Path

$edge = @(
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge was not found; it is what renders the SVG.' }

$work = Join-Path ([IO.Path]::GetTempPath()) ('md2onenote-icons-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
New-Item -ItemType Directory -Path $png, $addInImages -Force | Out-Null

function Invoke-Edge {
    param([string] $Url, [int] $Width, [int] $Height, [string] $Out)

    # A fresh profile per run keeps Edge from touching the user's; transparent default
    # background so the PNG keeps the SVG's alpha.
    $arguments = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run',
        '--default-background-color=00000000',
        "--user-data-dir=$work\profile",
        "--window-size=$Width,$Height",
        "--screenshot=$Out",
        $Url
    )
    $process = Start-Process -FilePath $edge -ArgumentList $arguments -PassThru -Wait -WindowStyle Hidden
    if ($process.ExitCode -ne 0 -or -not (Test-Path $Out)) { throw "Edge failed to render $Out" }
}

function Render-Size {
    param([int] $Size, [string] $Out)

    $html = Join-Path $work "icon-$Size.html"
    $svgUrl = 'file:///' + ($Source -replace '\\', '/')
    @"
<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;padding:0;background:transparent;overflow:hidden}
img{display:block;width:${Size}px;height:${Size}px}
</style></head><body><img src="$svgUrl"></body></html>
"@ | Set-Content -Path $html -Encoding utf8
    Invoke-Edge -Url ('file:///' + ($html -replace '\\', '/')) -Width $Size -Height $Size -Out $Out
}

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
foreach ($size in $sizes) {
    $out = Join-Path $png "icon-$size.png"
    Render-Size -Size $size -Out $out
    Write-Host "  $out"
}

# ---- ICO -----------------------------------------------------------------------------------

function Get-DibEntry {
    param([string] $PngPath)

    $bitmap = [System.Drawing.Bitmap]::new($PngPath)
    try {
        $w = $bitmap.Width; $h = $bitmap.Height
        $stream = [IO.MemoryStream]::new()
        $writer = [IO.BinaryWriter]::new($stream)

        # BITMAPINFOHEADER; biHeight counts the XOR and the AND mask together.
        $writer.Write([int32]40); $writer.Write([int32]$w); $writer.Write([int32]($h * 2))
        $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int32]0)
        $writer.Write([int32]($w * $h * 4)); $writer.Write([int32]0); $writer.Write([int32]0)
        $writer.Write([int32]0); $writer.Write([int32]0)

        # XOR: bottom-up rows of BGRA, straight from the bitmap.
        for ($y = $h - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $w; $x++) {
                $c = $bitmap.GetPixel($x, $y)
                $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
            }
        }

        # AND mask: 1 bit per pixel, rows padded to 4 bytes, all zero (alpha carries the shape).
        $rowBytes = [int](([Math]::Ceiling($w / 8) + 3) -band -bnot 3)
        $writer.Write([byte[]]::new($rowBytes * $h))

        $writer.Flush()
        return @{ Width = $w; Height = $h; Bytes = $stream.ToArray() }
    }
    finally {
        $bitmap.Dispose()
    }
}

function Get-PngEntry {
    param([string] $PngPath)

    $bitmap = [System.Drawing.Bitmap]::new($PngPath)
    try {
        return @{ Width = $bitmap.Width; Height = $bitmap.Height; Bytes = [IO.File]::ReadAllBytes($PngPath) }
    }
    finally {
        $bitmap.Dispose()
    }
}

$entries = @()
foreach ($size in 16, 24, 32, 48)  { $entries += Get-DibEntry -PngPath (Join-Path $png "icon-$size.png") }
foreach ($size in 64, 128, 256)    { $entries += Get-PngEntry -PngPath (Join-Path $png "icon-$size.png") }

$ico = Join-Path $brand 'icon.ico'
$stream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$entries.Count)

$offset = 6 + 16 * $entries.Count
foreach ($entry in $entries) {
    # Width and height are one byte each; 256 is written as 0.
    $writer.Write([byte]($entry.Width % 256)); $writer.Write([byte]($entry.Height % 256))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int32]$entry.Bytes.Length); $writer.Write([int32]$offset)
    $offset += $entry.Bytes.Length
}
foreach ($entry in $entries) { $writer.Write($entry.Bytes) }
$writer.Flush()
[IO.File]::WriteAllBytes($ico, $stream.ToArray())
Write-Host "  $ico"

# ---- ribbon images and social preview ------------------------------------------------------

Copy-Item (Join-Path $png 'icon-32.png') (Join-Path $addInImages 'Import32.png') -Force
Copy-Item (Join-Path $png 'icon-16.png') (Join-Path $addInImages 'Import16.png') -Force
Copy-Item (Join-Path $png 'icon-64.png') (Join-Path $addInImages 'Logo64.png') -Force
Write-Host "  $addInImages\Import32.png, Import16.png, Logo64.png"

$previewHtml = Join-Path $brand 'social-preview.html'
if (Test-Path $previewHtml) {
    $out = Join-Path $brand 'social-preview.png'
    Invoke-Edge -Url ('file:///' + ($previewHtml -replace '\\', '/')) -Width 1280 -Height 640 -Out $out
    Write-Host "  $out"
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Done.' -ForegroundColor Green
