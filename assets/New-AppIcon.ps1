# 生成应用图标 daedalus.ico（多分辨率，PNG 编码条目，Vista+ 均支持）
# 扁平纯色工具箱：主体 + 提手 + 箱盖分割线 + 锁扣（镂空），透明背景
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File assets/New-AppIcon.ps1
param(
    [string]$OutIco = (Join-Path $PSScriptRoot '..\src\Daedalus.App\daedalus.ico'),
    [string]$Color = '#DD6B20',
    [string]$PreviewDir = ''
)

Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# 以 256x256 为设计坐标系，按比例绘制到任意尺寸（抗锯齿）
function Draw-Toolbox([int]$size, [string]$pngPath) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($Color))
    $clear = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Transparent)

    # 提手：圆角矩形，内部镂空（镂空下缘与箱体顶边齐平，使提手与箱体相连）
    $g.FillPath($brush, (New-RoundedRectPath (88 * $s) (36 * $s) (80 * $s) (52 * $s) (16 * $s)))
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.FillPath($clear, (New-RoundedRectPath (104 * $s) (54 * $s) (48 * $s) (22 * $s) (10 * $s)))
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver

    # 箱体
    $g.FillPath($brush, (New-RoundedRectPath (16 * $s) (76 * $s) (224 * $s) (140 * $s) (18 * $s)))

    # 镂空：箱盖分割线 + 中央锁扣
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g.FillRectangle($clear, (16 * $s), (116 * $s), (224 * $s), [Math]::Max(1.0, 10 * $s))
    $g.FillPath($clear, (New-RoundedRectPath (114 * $s) (104 * $s) (28 * $s) (34 * $s) (8 * $s)))
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver

    $g.Dispose()
    $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "daedalus-icon-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    $pngs = @()
    foreach ($sz in $sizes) {
        $png = Join-Path $tmp "$sz.png"
        Draw-Toolbox $sz $png
        $pngs += , ([byte[]][System.IO.File]::ReadAllBytes($png))
    }

    # ICO 头 + 目录项 + PNG 数据
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)              # reserved
    $bw.Write([uint16]1)              # type: icon
    $bw.Write([uint16]$sizes.Count)   # image count
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $b = [byte]($sizes[$i] -band 0xFF)   # 256 按规范存 0
        $bw.Write($b)                        # width
        $bw.Write($b)                        # height
        $bw.Write([byte]0)                   # palette
        $bw.Write([byte]0)                   # reserved
        $bw.Write([uint16]1)                 # color planes
        $bw.Write([uint16]32)                # bits per pixel
        $bw.Write([uint32]$pngs[$i].Length)  # data size
        $bw.Write([uint32]$offset)           # data offset
        $offset += $pngs[$i].Length
    }
    foreach ($bytes in $pngs) { $bw.Write($bytes) }
    $bw.Flush()

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutIco) | Out-Null
    [System.IO.File]::WriteAllBytes($OutIco, $ms.ToArray())

    if ($PreviewDir -ne '') {
        New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
        foreach ($sz in $sizes) {
            Copy-Item (Join-Path $tmp "$sz.png") (Join-Path $PreviewDir "$sz.png") -Force
        }
    }

    Write-Host "已生成 $OutIco（$($sizes.Count) 个分辨率：$($sizes -join ', ')）"
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
