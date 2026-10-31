$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $rootDir "src\SpotifyDesktop\SpotifyDesktop.csproj"
$iconSource = Join-Path $rootDir "src\SpotifyDesktop\Assets\spotify.ico"
$publishDir = Join-Path $rootDir "publish\win-x64"
$distDir = Join-Path $rootDir "dist"
$installerAssetsDir = Join-Path $PSScriptRoot "assets"
$issPath = Join-Path $PSScriptRoot "Spotify.iss"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerAssetsDir | Out-Null

Write-Host "==> [1/4] Generating Inno Setup Wizard Branding Assets (BMP)..." -ForegroundColor Cyan
Add-Type -AssemblyName System.Drawing

function Draw-SpotifyLogo([System.Drawing.Graphics]$g, [float]$cx, [float]$cy, [float]$radius) {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Outer Green Circle (#1DB954)
    $greenBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 29, 185, 84))
    $g.FillEllipse($greenBrush, [float]($cx - $radius), [float]($cy - $radius), [float]($radius * 2.0), [float]($radius * 2.0))
    $greenBrush.Dispose()

    $left = [float]($cx - $radius)
    $top = [float]($cy - $radius)
    $s = [float]($radius * 2.0)
    $blackColor = [System.Drawing.Color]::FromArgb(255, 12, 12, 12)

    $pen1 = New-Object System.Drawing.Pen($blackColor, [Math]::Max(1.0, [float]($s * 0.090)))
    $pen1.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen1.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawBezier(
        $pen1,
        (New-Object System.Drawing.PointF([float]($left + $s * 0.235), [float]($top + $s * 0.365))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.430), [float]($top + $s * 0.300))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.665), [float]($top + $s * 0.325))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.805), [float]($top + $s * 0.425)))
    )
    $pen1.Dispose()

    $pen2 = New-Object System.Drawing.Pen($blackColor, [Math]::Max(1.0, [float]($s * 0.076)))
    $pen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawBezier(
        $pen2,
        (New-Object System.Drawing.PointF([float]($left + $s * 0.265), [float]($top + $s * 0.515))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.435), [float]($top + $s * 0.460))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.630), [float]($top + $s * 0.485))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.755), [float]($top + $s * 0.570)))
    )
    $pen2.Dispose()

    $pen3 = New-Object System.Drawing.Pen($blackColor, [Math]::Max(1.0, [float]($s * 0.064)))
    $pen3.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen3.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawBezier(
        $pen3,
        (New-Object System.Drawing.PointF([float]($left + $s * 0.295), [float]($top + $s * 0.658))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.440), [float]($top + $s * 0.612))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.600), [float]($top + $s * 0.632))),
        (New-Object System.Drawing.PointF([float]($left + $s * 0.710), [float]($top + $s * 0.705)))
    )
    $pen3.Dispose()
}

# 1. Generate wizard-banner.bmp (164 x 314, 24-bit RGB)
$bannerBmp = New-Object System.Drawing.Bitmap(164, 314, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$bg = [System.Drawing.Graphics]::FromImage($bannerBmp)
$bg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$bg.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$gradRect = New-Object System.Drawing.Rectangle(0, 0, 164, 314)
$gradBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $gradRect,
    [System.Drawing.Color]::FromArgb(255, 10, 10, 14),
    [System.Drawing.Color]::FromArgb(255, 8, 22, 14),
    90.0)
$bg.FillRectangle($gradBrush, $gradRect)

# Rounded Dynamic Island Pill preview at top of banner
$gp = New-Object System.Drawing.Drawing2D.GraphicsPath
$gp.AddArc(32, 22, 18, 18, 90, 180)
$gp.AddArc(114, 22, 18, 18, 270, 180)
$gp.CloseFigure()
$pillBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 0, 0))
$pillPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(65, 255, 255, 255), 1.0)
$bg.FillPath($pillBrush, $gp)
$bg.DrawPath($pillPen, $gp)

# Mini Spotify dot & waveform inside the banner pill
Draw-SpotifyLogo $bg 42.0 31.0 5.5
$waveBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 29, 185, 84))
$bg.FillRectangle($waveBrush, 110, 28, 2, 6)
$bg.FillRectangle($waveBrush, 114, 25, 2, 12)
$bg.FillRectangle($waveBrush, 118, 27, 2, 8)
$waveBrush.Dispose()
$gp.Dispose()

# Main Spotify Emblem in center
Draw-SpotifyLogo $bg 82.0 126.0 38.0

# Title & Subtitle on Banner
$titleFont = New-Object System.Drawing.Font("Segoe UI", 12.0, [System.Drawing.FontStyle]::Bold)
$subFont = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular)
$whiteTextBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 245, 245, 247))
$mutedTextBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 160, 160, 170))
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center

$bg.DrawString("Spotify Desktop", $titleFont, $whiteTextBrush, [float]82.0, [float]182.0, $sf)
$bg.DrawString("Ad-Free Desktop App", $subFont, $mutedTextBrush, [float]82.0, [float]206.0, $sf)
$bg.DrawString("+ Dynamic Island", $subFont, $mutedTextBrush, [float]82.0, [float]222.0, $sf)

$bannerPath = Join-Path $installerAssetsDir "wizard-banner.bmp"
$bannerBmp.Save($bannerPath, [System.Drawing.Imaging.ImageFormat]::Bmp)

$gradBrush.Dispose()
$pillBrush.Dispose()
$pillPen.Dispose()
$titleFont.Dispose()
$subFont.Dispose()
$whiteTextBrush.Dispose()
$mutedTextBrush.Dispose()
$sf.Dispose()
$bg.Dispose()
$bannerBmp.Dispose()

# 2. Generate wizard-small.bmp (55 x 55, 24-bit RGB)
$smallBmp = New-Object System.Drawing.Bitmap(55, 55, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$sg = [System.Drawing.Graphics]::FromImage($smallBmp)
$sg.Clear([System.Drawing.Color]::White)
Draw-SpotifyLogo $sg 27.5 27.5 22.0
$smallPath = Join-Path $installerAssetsDir "wizard-small.bmp"
$smallBmp.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$sg.Dispose()
$smallBmp.Dispose()

Write-Host "==> [2/4] Publishing Single-File Release..." -ForegroundColor Cyan

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=embedded `
    --output $publishDir

Copy-Item -Path $iconSource -Destination (Join-Path $publishDir "spotify.ico") -Force

Write-Host "==> [3/4] Locating Inno Setup Compiler (ISCC.exe)..." -ForegroundColor Cyan
function Find-IsccPath {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

$iscc = Find-IsccPath
if (-not $iscc) {
    Write-Host "Inno Setup 6 not found. Installing JRSoftware.InnoSetup via winget..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
    $iscc = Find-IsccPath
}

if (-not $iscc) {
    throw "Failed to locate ISCC.exe after installation."
}

Write-Host "==> [4/4] Compiling Inno Setup Installer ($issPath)..." -ForegroundColor Cyan
& $iscc $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC compilation failed with exit code $LASTEXITCODE"
}

$outputInstaller = Join-Path $distDir "Spotify_Setup_v1.0.0.exe"
$fileInfo = Get-Item $outputInstaller
Write-Host ""
Write-Host "SUCCESS! Installer created:" -ForegroundColor Green
Write-Host "  Path: $($fileInfo.FullName)" -ForegroundColor Green
Write-Host "  Size: $([Math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
