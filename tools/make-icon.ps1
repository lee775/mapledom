# MapleDom 앱 아이콘 생성기: 스톱워치 + 단풍잎. 결과: src/MapleDom.App/Assets/app.ico (16~256px, PNG 압축 ICO)
# 사용: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-PreviewDir artifacts\icon]
param(
    [string]$OutIco = (Join-Path $PSScriptRoot "..\src\MapleDom.App\Assets\app.ico"),
    [string]$PreviewDir = (Join-Path $PSScriptRoot "..\artifacts\icon")
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force (Split-Path $OutIco) | Out-Null
New-Item -ItemType Directory -Force $PreviewDir | Out-Null

function Color([string]$hex, [int]$a = 255) {
    [System.Drawing.Color]::FromArgb($a,
        [Convert]::ToInt32($hex.Substring(1, 2), 16),
        [Convert]::ToInt32($hex.Substring(3, 2), 16),
        [Convert]::ToInt32($hex.Substring(5, 2), 16))
}

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# 단풍잎 윤곽 (100x100 상자, 오른쪽 절반 정의 후 좌우 대칭)
$half = @(@(50, 0), @(59, 18), @(70, 10), @(66, 28), @(84, 22), @(78, 38), @(98, 46), @(80, 58), @(90, 76), @(66, 68), @(58, 86), @(53, 74), @(53, 100))
$leaf = @()
foreach ($pt in $half) { $leaf += , @($pt[0], $pt[1]) }
for ($i = $half.Count - 1; $i -ge 1; $i--) { $leaf += , @((100 - $half[$i][0]), $half[$i][1]) }

function DrawIcon([int]$size) {
    $ss = 4                      # 4배 슈퍼샘플링 후 축소
    $c = $size * $ss
    $m = $c / 256.0              # 256 기준 좌표계 → 캔버스
    $small = $size -lt 24

    $bmp = New-Object System.Drawing.Bitmap $c, $c
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 배경: 둥근 사각형, 어두운 남색 그라데이션
    $bgPath = RoundedRect (8 * $m) (8 * $m) (240 * $m) (240 * $m) (56 * $m)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList `
        (New-Object System.Drawing.PointF -ArgumentList 0, (8 * $m)), `
        (New-Object System.Drawing.PointF -ArgumentList 0, (248 * $m)), `
        (Color '#2c3749'), (Color '#131923')
    $g.FillPath($bgBrush, $bgPath)
    $borderPen = New-Object System.Drawing.Pen -ArgumentList (Color '#6b7c99' 150), (3 * $m)
    $g.DrawPath($borderPen, $bgPath)

    $orange = Color '#ffab4d'
    $orangeBrush = New-Object System.Drawing.SolidBrush -ArgumentList $orange

    # 스톱워치 크라운(위쪽 버튼)과 옆 버튼
    $crownW = if ($small) { 40 } else { 30 }
    $crown = RoundedRect ((128 - $crownW / 2) * $m) (18 * $m) ($crownW * $m) (22 * $m) (6 * $m)
    $g.FillPath($orangeBrush, $crown)
    $g.FillRectangle($orangeBrush, [float](120 * $m), [float](36 * $m), [float](16 * $m), [float](18 * $m))
    if (-not $small) {
        $state = $g.Save()
        $g.TranslateTransform(193 * $m, 64 * $m)
        $g.RotateTransform(45)
        $btn = RoundedRect (-11 * $m) (-9 * $m) (22 * $m) (18 * $m) (5 * $m)
        $g.FillPath($orangeBrush, $btn)
        $g.Restore($state)
    }

    # 링 + 문자판
    $cx = 128 * $m; $cy = 144 * $m; $r = 88 * $m
    $ringW = if ($small) { 22 } else { 16 }
    $g.FillEllipse($orangeBrush, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))
    $ri = $r - $ringW * $m
    $faceBrush = New-Object System.Drawing.SolidBrush -ArgumentList (Color '#0e131b')
    $g.FillEllipse($faceBrush, [float]($cx - $ri), [float]($cy - $ri), [float](2 * $ri), [float](2 * $ri))

    # 12/3/6/9 눈금
    if (-not $small) {
        $tickPen = New-Object System.Drawing.Pen -ArgumentList (Color '#ffab4d' 210), (6 * $m)
        $tickPen.StartCap = 'Round'; $tickPen.EndCap = 'Round'
        foreach ($ang in 0, 90, 180, 270) {
            $rad = $ang * [Math]::PI / 180
            $x1 = $cx + [Math]::Sin($rad) * ($ri - 6 * $m); $y1 = $cy - [Math]::Cos($rad) * ($ri - 6 * $m)
            $x2 = $cx + [Math]::Sin($rad) * ($ri - 20 * $m); $y2 = $cy - [Math]::Cos($rad) * ($ri - 20 * $m)
            $g.DrawLine($tickPen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
        }
    }

    # 단풍잎 (문자판 안)
    $leafSize = if ($small) { 104 } else { 94 }
    $ls = $leafSize * $m
    $lx = $cx - $ls / 2; $ly = $cy - $ls / 2 + 3 * $m
    $pts = [System.Drawing.PointF[]]($leaf | ForEach-Object {
        New-Object System.Drawing.PointF -ArgumentList ([float]($lx + $_[0] / 100.0 * $ls)), ([float]($ly + $_[1] / 100.0 * $ls))
    })
    $leafBrush = New-Object System.Drawing.SolidBrush -ArgumentList (Color '#ff6a3d')
    $g.FillPolygon($leafBrush, $pts)

    # 바늘 (2시 방향) + 중심점
    if (-not $small) {
        $rad = 60 * [Math]::PI / 180
        $hx = $cx + [Math]::Sin($rad) * 60 * $m; $hy = $cy - [Math]::Cos($rad) * 60 * $m
        $outline = New-Object System.Drawing.Pen -ArgumentList (Color '#0e131b' 210), (15 * $m)
        $outline.StartCap = 'Round'; $outline.EndCap = 'Round'
        $handPen = New-Object System.Drawing.Pen -ArgumentList (Color '#fff3dc'), (9 * $m)
        $handPen.StartCap = 'Round'; $handPen.EndCap = 'Round'
        $g.DrawLine($outline, [float]$cx, [float]$cy, [float]$hx, [float]$hy)
        $g.DrawLine($handPen, [float]$cx, [float]$cy, [float]$hx, [float]$hy)
        $dot = New-Object System.Drawing.SolidBrush -ArgumentList (Color '#fff3dc')
        $g.FillEllipse($dot, [float]($cx - 8 * $m), [float]($cy - 8 * $m), [float](16 * $m), [float](16 * $m))
    }
    $g.Dispose()

    $out = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'
    $g2.SmoothingMode = 'AntiAlias'
    $g2.PixelOffsetMode = 'HighQuality'
    $g2.CompositingQuality = 'HighQuality'
    $g2.DrawImage($bmp, 0, 0, $size, $size)
    $g2.Dispose()
    $bmp.Dispose()
    return $out
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$entries = @()
$bitmaps = @{}
foreach ($s in $sizes) {
    $bmp = DrawIcon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += , @($s, $ms.ToArray())
    $bitmaps[$s] = $bmp
}

# ICO 쓰기 (PNG 항목, Vista 이상)
$stream = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter -ArgumentList $stream
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e[0]; $data = $e[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($e in $entries) { $bw.Write($e[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $stream.ToArray())

# 미리보기 시트: 실제 크기 + 작은 크기 확대본, 어두운/밝은 배경
$sheet = New-Object System.Drawing.Bitmap 700, 420
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.Clear((Color '#1e1e1e'))
$sg.FillRectangle((New-Object System.Drawing.SolidBrush -ArgumentList (Color '#f3f3f3')), 0, 300, 700, 120)
$sg.InterpolationMode = 'NearestNeighbor'
$sg.PixelOffsetMode = 'Half'
$sg.DrawImage($bitmaps[256], 20, 20, 256, 256)
$sg.DrawImage($bitmaps[48], 300, 20, 48, 48)
$sg.DrawImage($bitmaps[32], 300, 90, 32, 32)
$sg.DrawImage($bitmaps[16], 300, 140, 16, 16)
$sg.DrawImage($bitmaps[32], 380, 20, 128, 128)   # 32px 4배 확대
$sg.DrawImage($bitmaps[16], 540, 20, 128, 128)   # 16px 8배 확대
$sg.DrawImage($bitmaps[48], 20, 330, 48, 48)
$sg.DrawImage($bitmaps[32], 90, 338, 32, 32)
$sg.DrawImage($bitmaps[16], 140, 346, 16, 16)
$sg.DrawImage($bitmaps[24], 180, 342, 24, 24)
$sg.Dispose()
$sheet.Save((Join-Path $PreviewDir "preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmaps[256].Save((Join-Path $PreviewDir "icon-256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
foreach ($b in $bitmaps.Values) { $b.Dispose() }

$check = New-Object System.Drawing.Icon -ArgumentList $OutIco
Write-Output ("ICO written: {0} ({1} bytes, {2} sizes), default frame {3}x{4}" -f $OutIco, (Get-Item $OutIco).Length, $entries.Count, $check.Width, $check.Height)
Write-Output ("Preview: {0}" -f (Join-Path $PreviewDir "preview.png"))
