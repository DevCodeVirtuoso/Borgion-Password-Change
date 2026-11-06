# Genereert src\Assets\app.ico (Borgion-slotje: navy vlak met amber hangslot).
# Gebruik: powershell -ExecutionPolicy Bypass -File tools\Make-Icon.ps1
Add-Type -AssemblyName System.Drawing

$out   = Join-Path $PSScriptRoot '..\src\Assets\app.ico'
$navy  = [System.Drawing.Color]::FromArgb(255, 0x0B, 0x3D, 0x5C)
$amber = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xA9, 0x00)
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 256.0

    # Achtergrond
    $bg = New-RoundRect 0 0 $s $s (48 * $k)
    $g.FillPath((New-Object System.Drawing.SolidBrush $navy), $bg)

    # Beugel van het slot
    $pen = New-Object System.Drawing.Pen $amber, ([Math]::Max(2, 26 * $k))
    $g.DrawArc($pen, 78 * $k, 44 * $k, 100 * $k, 110 * $k, 180, 180)
    $g.DrawLine($pen, 78 * $k, 99 * $k, 78 * $k, 124 * $k)
    $g.DrawLine($pen, 178 * $k, 99 * $k, 178 * $k, 124 * $k)

    # Body
    $body = New-RoundRect (52 * $k) (116 * $k) (152 * $k) (104 * $k) (18 * $k)
    $g.FillPath((New-Object System.Drawing.SolidBrush $amber), $body)

    # Sleutelgat
    if ($s -ge 24) {
        $nb = New-Object System.Drawing.SolidBrush $navy
        $g.FillEllipse($nb, 113 * $k, 140 * $k, 30 * $k, 30 * $k)
        $g.FillRectangle($nb, 121 * $k, 160 * $k, 14 * $k, 36 * $k)
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO-bestand met PNG-gecomprimeerde afbeeldingen (Windows Vista+)
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
Write-Host "Icoon geschreven naar $out"
