# Desenha o ícone do Lume (folha de vidro com régua luminosa) e grava:
#   lume.ico  (16 a 256 px, usado no Lume.exe e no Gerenciador de Tarefas)
#   lume.png  (256 px, usado pela janela e pela barra de tarefas)
param([string]$OutDir = $PSScriptRoot)
Add-Type -AssemblyName System.Drawing

function C($a, $r, $g, $b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
function P($x, $y) { [System.Drawing.PointF]::new($x, $y) }
function LG($x1, $y1, $x2, $y2, $c1, $c2) { [System.Drawing.Drawing2D.LinearGradientBrush]::new((P $x1 $y1), (P $x2 $y2), $c1, $c2) }
function RR($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = 2 * $r
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); $p
}
function RoundPen($brush, $w) { $pen = [System.Drawing.Pen]::new($brush, $w); $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'; $pen }
function Bar($g, $brush, $x, $y, $w, $h) { $p = RR $x ($y - $h / 2) $w $h ($h / 2); $g.FillPath($brush, $p) }

$S = 1024
$bmp = [System.Drawing.Bitmap]::new($S, $S)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
$g.Clear([System.Drawing.Color]::Transparent)

# 1. Fundo: quadrado arredondado grafite
$bg = RR 24 24 976 976 230
$g.FillPath((LG 0 24 0 1000 (C 255 66 71 92) (C 255 27 31 44)), $bg)
$g.DrawPath([System.Drawing.Pen]::new((LG 0 24 0 500 (C 70 255 255 255) (C 0 255 255 255)), 5), $bg)

# 2. Folha de vidro com canto dobrado
$dx = 239; $dy = 150; $dw = 547; $dh = 724; $dr = 52; $fold = 160
$doc = New-Object System.Drawing.Drawing2D.GraphicsPath
$doc.AddArc($dx, $dy, 2 * $dr, 2 * $dr, 180, 90)
$doc.AddLine($dx + $dr, $dy, $dx + $dw - $fold, $dy)
$doc.AddLine($dx + $dw - $fold, $dy, $dx + $dw, $dy + $fold)
$doc.AddArc($dx + $dw - 2 * $dr, $dy + $dh - 2 * $dr, 2 * $dr, 2 * $dr, 0, 90)
$doc.AddArc($dx, $dy + $dh - 2 * $dr, 2 * $dr, 2 * $dr, 90, 90)
$doc.CloseFigure()
for ($i = 8; $i -ge 1; $i--) { $g.DrawPath((RoundPen ([System.Drawing.SolidBrush]::new((C 9 90 210 255))) ($i * 9)), $doc) }  # brilho ciano
$g.FillPath((LG $dx $dy ($dx + $dw) ($dy + $dh) (C 240 128 205 226) (C 240 38 92 196)), $doc)
$g.FillPath((LG 0 ($dy - 2) 0 ($dy + $dh * 0.55) (C 40 255 255 255) (C 0 255 255 255)), $doc)            # reflexo do vidro
$g.DrawPath((RoundPen (LG $dx $dy ($dx + $dw) ($dy + $dh) (C 235 215 250 255) (C 200 90 200 255)) 9), $doc)
$corner = New-Object System.Drawing.Drawing2D.GraphicsPath
$fx = $dx + $dw - $fold
$corner.AddLine($fx, $dy, $fx, $dy + $fold - 60)
$corner.AddBezier($fx, $dy + $fold - 60, $fx, $dy + $fold - 10, $fx + 10, $dy + $fold, $fx + 60, $dy + $fold)
$corner.AddLine($fx + 60, $dy + $fold, $dx + $dw, $dy + $fold)
$corner.CloseFigure()
$g.FillPath((LG $fx $dy ($dx + $dw) ($dy + $fold) (C 255 150 245 255) (C 255 30 190 235)), $corner)
$g.DrawPath((RoundPen ([System.Drawing.SolidBrush]::new((C 200 225 252 255))) 6), $corner)

# 3. Linhas de texto
$line = [System.Drawing.SolidBrush]::new((C 135 225 242 255))
$lx = 326
foreach ($l in @(@(283, 182), @(360, 240), @(589, 262), @(666, 376), @(743, 240))) { Bar $g $line $lx $l[0] $l[1] 24 }
Bar $g ([System.Drawing.SolidBrush]::new((C 70 225 242 255))) $lx 443 376 24

# 4. Régua luminosa (amarelo -> ciano)
$rx = 131; $ry = 424; $rw = 763; $rh = 178
$ruler = RR $rx $ry $rw $rh 30
$yel = C 255 255 214 64; $cya = C 255 96 228 250
# (o degradê do brilho cobre além da régua para não repetir a cor nas pontas)
for ($i = 12; $i -ge 1; $i--) { $g.DrawPath((RoundPen (LG ($rx - 140) 0 ($rx + $rw + 140) 0 (C 13 255 205 50) (C 13 70 220 255)) ($i * 10)), $ruler) }
$g.FillPath((LG ($rx - 2) 0 ($rx + $rw + 2) 0 (C 130 240 186 50) (C 120 60 196 225)), $ruler)
$g.FillPath((LG 0 ($ry - 2) 0 ($ry + $rh + 2) (C 55 255 255 255) (C 0 255 255 255)), $ruler)
$g.DrawPath((RoundPen (LG ($rx - 20) 0 ($rx + $rw + 20) 0 $yel $cya) 11), $ruler)
$tick = RoundPen (LG ($rx - 20) 0 ($rx + $rw + 20) 0 (C 255 255 226 110) (C 255 150 238 255)) 13
$n = 9
for ($i = 0; $i -lt $n; $i++) {
  $x = $rx + 66 + $i * (($rw - 132) / ($n - 1))
  $len = if ($i % 2 -eq 0) { 48 } else { 34 }
  $g.DrawLine($tick, $x, $ry + 18, $x, $ry + 18 + $len)
}
# Linha em destaque sob a régua
$hy = $ry + 116
for ($i = 10; $i -ge 1; $i--) { $g.DrawLine((RoundPen (LG ($lx - 100) 0 ($lx + 476) 0 (C 18 255 220 80) (C 18 255 250 210)) (28 + $i * 9)), $lx + 14, $hy, $lx + 362, $hy) }
$g.DrawLine((RoundPen (LG ($lx - 10) 0 ($lx + 386) 0 (C 255 255 222 70) (C 255 255 252 230)) 28), $lx + 14, $hy, $lx + 362, $hy)
$g.Dispose()

# Reduções sucessivas deixam os tamanhos pequenos mais nítidos
function Shrink($src, $s) {
  $b = [System.Drawing.Bitmap]::new($s, $s); $gg = [System.Drawing.Graphics]::FromImage($b)
  $gg.InterpolationMode = 'HighQualityBicubic'; $gg.PixelOffsetMode = 'HighQuality'; $gg.CompositingQuality = 'HighQuality'
  $gg.DrawImage($src, 0, 0, $s, $s); $gg.Dispose(); $b
}
$b512 = Shrink $bmp 512
$b256 = Shrink $b512 256
$b128 = Shrink $b256 128
$b64 = Shrink $b128 64
$b48 = Shrink $b128 48
$sizes = @(256, 64, 48, 32, 24, 16)
$imgs = @($b256, $b64, $b48, (Shrink $b64 32), (Shrink $b48 24), (Shrink (Shrink $b64 32) 16))
$b256.Save((Join-Path $OutDir 'lume.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$b512.Save((Join-Path $OutDir 'lume-512.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$pngs = @()
foreach ($im in $imgs) {
  $ms = New-Object System.IO.MemoryStream
  $im.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += , ($ms.ToArray())
}
$fs = [System.IO.File]::Create((Join-Path $OutDir 'lume.ico'))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
