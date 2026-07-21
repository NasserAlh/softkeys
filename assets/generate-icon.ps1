# softkeys icon pipeline (F-15, D-15): assets/softkeys.svg -> assets/softkeys.ico
#   powershell -ExecutionPolicy Bypass -File assets\generate-icon.ps1
# Pure .NET (WPF) conversion — no ImageMagick or network dependency. The SVG's
# glyphs are baked path data, so output is identical on any machine. Renders
# icon-small at 16/24 px and icon-full at 32/48/64/128/256 px, then assembles
# the ICO: 32bpp BMP entries below 256 px, PNG entry at 256 px (Vista+ format).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$svgPath = Join-Path $PSScriptRoot 'softkeys.svg'
$icoPath = Join-Path $PSScriptRoot 'softkeys.ico'
[xml]$svg = Get-Content $svgPath -Raw
$viewBox = 256.0  # fixed by the source document

function New-Brush([string]$hex, [double]$opacity = 1.0) {
    $c = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    if ($opacity -lt 1.0) { $c.A = [byte][math]::Round($opacity * 255) }
    $b = New-Object System.Windows.Media.SolidColorBrush $c
    $b.Freeze(); $b
}

# Draw one SVG group's rect/path children (the subset this document uses).
function Draw-Group([System.Windows.Media.DrawingContext]$dc, $group) {
    foreach ($el in $group.ChildNodes) {
        switch ($el.LocalName) {
            'rect' {
                $rect = [System.Windows.Rect]::new([double]$el.x, [double]$el.y, [double]$el.width, [double]$el.height)
                $pen = $null
                if ($el.stroke) {
                    $op = if ($el.'stroke-opacity') { [double]$el.'stroke-opacity' } else { 1.0 }
                    $pen = New-Object System.Windows.Media.Pen (New-Brush $el.stroke $op), ([double]$el.'stroke-width')
                }
                $dc.DrawRoundedRectangle((New-Brush $el.fill), $pen, $rect, [double]$el.rx, [double]$el.rx)
            }
            'path' {
                $geo = [System.Windows.Media.Geometry]::Parse('F1' + $el.d)
                $dc.DrawGeometry((New-Brush $el.fill), $null, $geo)
            }
        }
    }
}

function Render-Group([string]$groupId, [int]$size) {
    $group = $svg.svg.g | Where-Object { $_.id -eq $groupId }
    if (-not $group) { throw "group '$groupId' not found in softkeys.svg" }
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / $viewBox), ($size / $viewBox)))
    Draw-Group $dc $group
    $dc.Pop(); $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $bmp
}

function Get-PngBytes($bitmapSource) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmapSource))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms); ,$ms.ToArray()   # unary comma: return byte[] intact, not enumerated
}

function Get-BmpIconBytes($bitmapSource, [int]$size) {
    $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bitmapSource, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
    $stride = $size * 4
    $px = New-Object byte[] ($stride * $size)
    $conv.CopyPixels($px, $stride, 0)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$size); $bw.Write([int]($size * 2))  # XOR+AND height
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($stride * $size)); $bw.Write([int]0); $bw.Write([int]0)
    $bw.Write([int]0); $bw.Write([int]0)
    for ($row = $size - 1; $row -ge 0; $row--) { $bw.Write($px, $row * $stride, $stride) }
    $andStride = [int]([math]::Ceiling($size / 32.0) * 4)
    $bw.Write((New-Object byte[] ($andStride * $size)))  # AND mask zeroed; alpha rules
    $bw.Flush(); ,$ms.ToArray()
}

$plan = @(
    @{ Size = 16;  Group = 'icon-small' }
    @{ Size = 24;  Group = 'icon-small' }
    @{ Size = 32;  Group = 'icon-full' }
    @{ Size = 48;  Group = 'icon-full' }
    @{ Size = 64;  Group = 'icon-full' }
    @{ Size = 128; Group = 'icon-full' }
    @{ Size = 256; Group = 'icon-full' }
)

$entries = foreach ($p in $plan) {
    $bmp = Render-Group $p.Group $p.Size
    $data = if ($p.Size -ge 256) { Get-PngBytes $bmp } else { Get-BmpIconBytes $bmp $p.Size }
    [pscustomobject]@{ Size = $p.Size; Data = $data }
}

$fs = [System.IO.File]::Create($icoPath)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $w.Write([byte]($e.Size -band 0xFF)); $w.Write([byte]($e.Size -band 0xFF))  # 0 = 256
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$e.Data.Length); $w.Write([int]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $w.Write([byte[]]$e.Data) }
$w.Flush(); $fs.Close()
Write-Output "wrote $icoPath ($((Get-Item $icoPath).Length) bytes, $($entries.Count) entries: $(($plan.Size) -join '/'))"
