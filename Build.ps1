<#
.SYNOPSIS
    Ricostruisce l'icona (WinTools.ico) e l'eseguibile (WinTools.exe) di WinTools.
.DESCRIPTION
    1. Disegna l'icona in vettoriale con WPF e la esporta in un .ico multi-risoluzione (PNG 16-256 px).
    2. Compila il progetto con l'SDK .NET (dotnet build, Release) per .NET Framework 4.8, già incluso in
       Windows 10 e 11: ne esce un unico WinTools.exe portatile, copiato nella cartella del progetto.
    Serve l'SDK .NET (https://dotnet.microsoft.com/download) con il targeting pack .NET Framework 4.8
    (lo scarica da solo da NuGet se manca).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Build.ps1
#>
param(
    [switch]$IconOnly,
    [switch]$SkipIcon,
    [string]$PreviewDir
)

$ErrorActionPreference = 'Stop'

# WPF richiede un thread STA: rilancia se necessario (es. PowerShell 7)
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-File', $PSCommandPath)
    if ($IconOnly) { $argList += '-IconOnly' }
    if ($SkipIcon) { $argList += '-SkipIcon' }
    if ($PreviewDir) { $argList += @('-PreviewDir', $PreviewDir) }
    & powershell.exe @argList
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$root = $PSScriptRoot
$icoPath = Join-Path $root 'WinTools.ico'

# ---------------------------------------------------------------- icona
# Una tessera con la barra in alto (tre tasti) e, sotto, la lente della ricerca: a 16 px resta solo la barra.
function Get-IconXaml {
    param([bool]$Detailed)
    if ($Detailed) {
        $content = @"
<Border Canvas.Left="40" Canvas.Top="44" Width="176" Height="52" CornerRadius="14" Background="#F2FFFFFF">
  <Border.Effect><DropShadowEffect BlurRadius="8" ShadowDepth="2" Direction="270" Opacity="0.25" Color="#002040"/></Border.Effect>
</Border>
<Rectangle Canvas.Left="56" Canvas.Top="58" Width="24" Height="24" RadiusX="6" RadiusY="6" Fill="#1E88E5"/>
<Rectangle Canvas.Left="92" Canvas.Top="58" Width="24" Height="24" RadiusX="6" RadiusY="6" Fill="#00ACC1"/>
<Rectangle Canvas.Left="128" Canvas.Top="58" Width="24" Height="24" RadiusX="6" RadiusY="6" Fill="#43A047"/>
<Rectangle Canvas.Left="164" Canvas.Top="58" Width="36" Height="24" RadiusX="6" RadiusY="6" Fill="#FFB300"/>
<Ellipse Canvas.Left="84" Canvas.Top="118" Width="68" Height="68" Stroke="#FFFFFFFF" StrokeThickness="16"/>
<Line X1="142" Y1="176" X2="186" Y2="220" Stroke="#FFFFFFFF" StrokeThickness="18" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
"@
    } else {
        # Dimensioni piccole: solo la barra con tre tasti grandi
        $content = @"
<Border Canvas.Left="36" Canvas.Top="60" Width="184" Height="136" CornerRadius="26" Background="#F2FFFFFF"/>
<Rectangle Canvas.Left="56" Canvas.Top="84" Width="44" Height="88" RadiusX="12" RadiusY="12" Fill="#1E88E5"/>
<Rectangle Canvas.Left="106" Canvas.Top="84" Width="44" Height="88" RadiusX="12" RadiusY="12" Fill="#00ACC1"/>
<Rectangle Canvas.Left="156" Canvas.Top="84" Width="44" Height="88" RadiusX="12" RadiusY="12" Fill="#FFB300"/>
"@
    }

    @"
<Canvas xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="256" Height="256">
  <Rectangle Canvas.Left="16" Canvas.Top="16" Width="224" Height="224" RadiusX="56" RadiusY="56">
    <Rectangle.Fill>
      <LinearGradientBrush StartPoint="0.1,0" EndPoint="0.9,1">
        <GradientStop Color="#2BD4C4" Offset="0"/>
        <GradientStop Color="#1E88E5" Offset="0.55"/>
        <GradientStop Color="#3949AB" Offset="1"/>
      </LinearGradientBrush>
    </Rectangle.Fill>
    <Rectangle.Effect><DropShadowEffect BlurRadius="14" ShadowDepth="5" Direction="270" Opacity="0.35" Color="#001A4D"/></Rectangle.Effect>
  </Rectangle>
  <Rectangle Canvas.Left="16" Canvas.Top="16" Width="224" Height="224" RadiusX="56" RadiusY="56">
    <Rectangle.Fill>
      <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
        <GradientStop Color="#40FFFFFF" Offset="0"/>
        <GradientStop Color="#00FFFFFF" Offset="0.5"/>
      </LinearGradientBrush>
    </Rectangle.Fill>
  </Rectangle>
  <Rectangle Canvas.Left="17" Canvas.Top="17" Width="222" Height="222" RadiusX="55" RadiusY="55" StrokeThickness="2">
    <Rectangle.Stroke>
      <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
        <GradientStop Color="#70FFFFFF" Offset="0"/>
        <GradientStop Color="#08FFFFFF" Offset="0.6"/>
      </LinearGradientBrush>
    </Rectangle.Stroke>
  </Rectangle>
  $content
</Canvas>
"@
}

function ConvertTo-PngBytes {
    param([string]$Xaml, [int]$Size)
    $canvas = [Windows.Markup.XamlReader]::Parse($Xaml)
    $box = New-Object Windows.Controls.Viewbox
    $box.Stretch = 'Uniform'; $box.Width = $Size; $box.Height = $Size
    $box.Child = $canvas
    $box.Measure([Windows.Size]::new($Size, $Size))
    $box.Arrange([Windows.Rect]::new(0, 0, $Size, $Size))
    $box.UpdateLayout()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($box)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    , $ms.ToArray()
}

function Write-IcoFile {
    param([string]$Path, [hashtable]$Frames)   # dimensione -> byte[] PNG
    $sizes = $Frames.Keys | Sort-Object
    $ms = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter($ms)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    foreach ($s in $sizes) {
        $png = $Frames[$s]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32)
        $bw.Write([uint32]$png.Length); $bw.Write([uint32]$offset)
        $offset += $png.Length
    }
    foreach ($s in $sizes) { $bw.Write($Frames[$s]) }
    $bw.Flush()
    [IO.File]::WriteAllBytes($Path, $ms.ToArray())
}

if (-not $SkipIcon) {
    Write-Host '[1/2] Generazione icona...' -ForegroundColor Cyan
    $frames = @{}
    foreach ($s in 16, 20, 24, 32, 40, 48, 64, 96, 128, 256) {
        $frames[$s] = ConvertTo-PngBytes -Xaml (Get-IconXaml -Detailed ($s -ge 40)) -Size $s
    }
    Write-IcoFile -Path $icoPath -Frames $frames
    Write-Host "      $icoPath ($((Get-Item $icoPath).Length) byte, $($frames.Count) risoluzioni)"
    if ($PreviewDir) {
        New-Item -ItemType Directory -Force $PreviewDir | Out-Null
        foreach ($s in 256, 48, 32, 16) { [IO.File]::WriteAllBytes((Join-Path $PreviewDir "icon-$s.png"), $frames[$s]) }
    }
    if ($IconOnly) { return }
}

# ---------------------------------------------------------------- eseguibile
Write-Host '[2/2] Compilazione WinTools.exe...' -ForegroundColor Cyan
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "Serve l'SDK .NET: https://dotnet.microsoft.com/download" }

# Unica fonte della versione: <Version> in WinTools.csproj -> assembly e manifest
$appVer = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'WinTools.csproj')), '<Version>([\d.]+)</Version>').Groups[1].Value
if (-not $appVer) { throw 'Versione non trovata in WinTools.csproj (<Version>).' }
$p = @($appVer.Split('.')) + @('0', '0', '0'); $fullVer = ($p[0..3] -join '.')
$manifest = Join-Path $root 'app.manifest'
$txt = [IO.File]::ReadAllText($manifest)
$new = $txt -replace '(assemblyIdentity version=")[\d.]+', "`${1}$fullVer"
if ($new -ne $txt) { [IO.File]::WriteAllText($manifest, $new, (New-Object Text.UTF8Encoding($false))) }
Write-Host "      versione $appVer"

& dotnet build (Join-Path $root 'WinTools.csproj') -c Release --nologo -v quiet "-p:Version=$appVer" "-p:FileVersion=$fullVer" "-p:AssemblyVersion=$fullVer"
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita (codice $LASTEXITCODE)." }

$built = Join-Path $root 'bin\Release\WinTools.exe'
$exePath = Join-Path $root 'WinTools.exe'
Copy-Item -LiteralPath $built -Destination $exePath -Force
Write-Host "      $exePath ($((Get-Item $exePath).Length) byte)" -ForegroundColor Green
Write-Host 'Fatto.' -ForegroundColor Green
