# VolumeKeeper.ico を生成する（スピーカーと音量バーを描いた複数サイズのアイコン）
Add-Type -AssemblyName System.Drawing
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = 16, 24, 32, 48, 64, 256

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    # 角丸の背景
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 14 * $s; $w = $size - 1
    $bg.AddArc(0, 0, $r, $r, 180, 90); $bg.AddArc($w - $r, 0, $r, $r, 270, 90)
    $bg.AddArc($w - $r, $w - $r, $r, $r, 0, 90); $bg.AddArc(0, $w - $r, $r, $r, 90, 90)
    $bg.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(0, $size)), ([System.Drawing.Color]::FromArgb(255, 38, 120, 210)), ([System.Drawing.Color]::FromArgb(255, 20, 70, 150))
    $g.FillPath($brush, $bg)

    # スピーカー
    $white = [System.Drawing.Brushes]::White
    $speaker = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(10 * $s, 24 * $s), [System.Drawing.PointF]::new(19 * $s, 24 * $s),
        [System.Drawing.PointF]::new(30 * $s, 13 * $s), [System.Drawing.PointF]::new(30 * $s, 51 * $s),
        [System.Drawing.PointF]::new(19 * $s, 40 * $s), [System.Drawing.PointF]::new(10 * $s, 40 * $s))
    $g.FillPolygon($white, $speaker)

    # 音量バー（5段のうち2段だけ点灯＝40%）
    $dim = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $lit = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 120, 230, 140))
    for ($i = 0; $i -lt 5; $i++) {
        $h = (8 + $i * 7) * $s
        $x = (35 + $i * 5.4) * $s
        $y = 52 * $s - $h
        $g.FillRectangle($(if ($i -lt 2) { $lit } else { $dim }), [single]$x, [single]$y, [single](3.8 * $s), [single]$h)
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

# ICO形式（PNG埋め込み）で書き出す
$images = foreach ($size in $sizes) { , (New-IconPng $size) }
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $here 'VolumeKeeper.ico'), $out.ToArray())
