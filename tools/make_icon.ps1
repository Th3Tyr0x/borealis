# Erzeugt das Programmsymbol src\Borealis.App\Assets\Borealis.ico (Polarstern über zwei roten Lichtbögen auf
# dunkler Kachel) in allen Windows-Größen. Kleine Größen bekommen dickere Linien, damit sie lesbar bleiben.
# Aufruf mit Windows PowerShell 5.1 (WPF zum Zeichnen): powershell -File tools\make_icon.ps1
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\Borealis.App\Assets\Borealis.ico'),
    # Optional: Vorschau aller Größen auf hellem und dunklem Grund als PNG
    [string]$Preview,
    # Kleines Bild für den Installer (64 px, PNG)
    [string]$WizardImage = (Join-Path $PSScriptRoot '..\setup\wizard-small.png')
)

Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'

function Brush([string]$hex) {
    $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
    $b.Freeze(); $b
}
$tile = Brush '#16161B'
$star = Brush '#ECECEF'
$red = Brush '#FF1F44'
$redSoft = Brush '#94192F'   # Rot zu 55 % auf der Kachel, wie die zweite Linie in der Seitenleiste

function Render([int]$size) {
    # Linienstärke im 48er-Raster: klein dicker
    $arc = if ($size -le 20) { 6.5 } elseif ($size -le 32) { 5 } else { 3.75 }
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / 48.0), ($size / 48.0)))
    $dc.DrawRoundedRectangle($tile, $null, (New-Object System.Windows.Rect 0, 0, 48, 48), 10, 10)
    $starGeo = [System.Windows.Media.Geometry]::Parse($(if ($size -le 20) { 'M24,3 L28,12 L37,16 L28,20 L24,30 L20,20 L11,16 L20,12 Z' } else { 'M24,5 L27,13 L35,16 L27,19 L24,27 L21,19 L13,16 L21,13 Z' }))
    $dc.DrawGeometry($star, $null, $starGeo)
    $pen = New-Object System.Windows.Media.Pen $red, $arc
    $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round'
    $dc.DrawGeometry($null, $pen, [System.Windows.Media.Geometry]::Parse('M8,38 Q24,25 40,38'))
    if ($size -gt 20) {
        $pen2 = New-Object System.Windows.Media.Pen $redSoft, $arc
        $pen2.StartLineCap = 'Round'; $pen2.EndLineCap = 'Round'
        $dc.DrawGeometry($null, $pen2, [System.Windows.Media.Geometry]::Parse('M14,43.5 Q24,35.5 34,43.5'))
    }
    $dc.Pop()
    $dc.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object System.IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Render $s) }

# ICO: Kopf, ein Eintrag je Größe, dann die PNG-Daten (PNG-Einträge versteht Windows ab Vista)
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $data = $images[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $w.Write($data) }
$w.Flush()
New-Item -ItemType Directory -Force (Split-Path $Output) | Out-Null
[IO.File]::WriteAllBytes($Output, $out.ToArray())

Write-Host "Symbol: $Output"
if ($WizardImage) {
    [IO.File]::WriteAllBytes($WizardImage, $images[[array]::IndexOf($sizes, 64)])
    Write-Host "Installer-Bild: $WizardImage"
}
if (-not $Preview) { return }
$x = 0; $panel = New-Object System.Windows.Media.DrawingVisual
$pdc = $panel.RenderOpen()
$pdc.DrawRectangle((Brush '#F0F0F3'), $null, (New-Object System.Windows.Rect 0, 0, 760, 280))
$pdc.DrawRectangle((Brush '#202024'), $null, (New-Object System.Windows.Rect 0, 140, 760, 140))
foreach ($i in 0..($sizes.Count - 1)) {
    $img = New-Object System.Windows.Media.Imaging.BitmapImage
    $img.BeginInit(); $img.StreamSource = New-Object System.IO.MemoryStream (, $images[$i]); $img.EndInit()
    $s = $sizes[$i]; if ($s -gt 128) { continue }
    $pdc.DrawImage($img, (New-Object System.Windows.Rect (10 + $x), (70 - $s / 2), $s, $s))
    $pdc.DrawImage($img, (New-Object System.Windows.Rect (10 + $x), (210 - $s / 2), $s, $s))
    $x += $s + 16
}
$pdc.Close()
$pb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap 760, 280, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$pb.Render($panel)
$penc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$penc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($pb))
$fs = [IO.File]::Create($Preview); $penc.Save($fs); $fs.Close()
Write-Host "Vorschau: $Preview"
