<#
.SYNOPSIS
	One-click installer builder for Wedding Music Planner Pro.

.DESCRIPTION
	Publishes the WPF app as a self-contained win-x64 build, then compiles a
	standard Windows Setup.exe using Inno Setup 6.

	If Inno Setup 6 is not installed it is downloaded and installed silently
	(no prompts, no interaction required from you).

	Run from anywhere — the only requirement is the .NET 8 SDK:
		powershell -ExecutionPolicy Bypass -File Installer\build-setup.ps1

.PARAMETER Version
	Installer version (e.g. 1.2.0). Default: 1.0.0

.PARAMETER Configuration
	Build configuration. Default: Release

.PARAMETER KeepStage
	If set, the intermediate publish folder is not deleted after the build.
#>
[CmdletBinding()]
param(
	[string]$Version       = "1.0.0",
	[string]$Configuration = "Release",
	[switch]$KeepStage
)

$ErrorActionPreference = "Stop"

# ── Paths ────────────────────────────────────────────────────────────────────
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Project    = Join-Path $RepoRoot "WeddingMusicPlannerPro.Wpf\WeddingMusicPlannerPro.Wpf.csproj"
$IssScript  = Join-Path $PSScriptRoot "wedding-music-planner.iss"
$PublishDir = Join-Path $RepoRoot "artifacts\publish-win-x64"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$Rid        = "win-x64"

# Version used by Inno Setup must be numeric — strip any pre-release suffix
$InnoVersion = ($Version -replace '-.*$','').Trim()
if ($InnoVersion -notmatch '^\d+(\.\d+){0,3}$') {
	throw "Version '$Version' could not be reduced to a numeric form for Inno Setup."
}
# Inno Setup requires exactly Major.Minor.Patch.Build — pad with .0 as needed
$parts = $InnoVersion.Split('.')
while ($parts.Count -lt 4) { $parts += '0' }
$InnoVersion4 = $parts[0..3] -join '.'

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Wedding Music Planner Pro - Setup builder"            -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ("  Version : {0}  (Inno: {1})" -f $Version, $InnoVersion4)
Write-Host ("  Config  : {0}" -f $Configuration)
Write-Host ("  Runtime : {0} (self-contained, no prerequisites)" -f $Rid)
Write-Host ""

# ── Step 1: Ensure Inno Setup 6 is available ─────────────────────────────────
$IsccExe = $null
$CandidatePaths = @(
	"${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
	"${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
foreach ($p in $CandidatePaths) {
	if (Test-Path $p) { $IsccExe = $p; break }
}

if (-not $IsccExe) {
	Write-Host "==> Inno Setup 6 not found — downloading and installing silently..." -ForegroundColor Yellow
	$InnoInstaller = Join-Path $env:TEMP "innosetup6-setup.exe"
	# Official download URL for Inno Setup 6 (latest stable at time of writing)
	$InnoDownloadUrl = "https://files.jrsoftware.org/is/6/innosetup-6.3.3.exe"
	Write-Host "    Downloading from $InnoDownloadUrl ..."
	Invoke-WebRequest -Uri $InnoDownloadUrl -OutFile $InnoInstaller -UseBasicParsing
	Write-Host "    Installing Inno Setup silently ..."
	Start-Process -FilePath $InnoInstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -Wait
	Remove-Item $InnoInstaller -Force -ErrorAction SilentlyContinue
	# Verify installation succeeded
	foreach ($p in $CandidatePaths) {
		if (Test-Path $p) { $IsccExe = $p; break }
	}
	if (-not $IsccExe) {
		throw "Inno Setup installation appeared to succeed but ISCC.exe was not found.`nTried: $($CandidatePaths -join ', ')"
	}
	Write-Host "    Inno Setup installed: $IsccExe" -ForegroundColor Green
} else {
	Write-Host "==> Using Inno Setup: $IsccExe" -ForegroundColor Green
}

# ── Step 2: dotnet publish (self-contained, win-x64) ─────────────────────────
Write-Host ""
Write-Host "==> Publishing self-contained $Rid build..." -ForegroundColor Cyan

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir   -Force | Out-Null
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

dotnet publish $Project `
	--configuration $Configuration `
	--runtime $Rid `
	--self-contained true `
	-p:PublishSingleFile=false `
	-p:Version=$Version `
	--output $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }
Write-Host "    Publish succeeded -> $PublishDir" -ForegroundColor Green

# ── Step 3: Generate a placeholder icon if none is present ───────────────────
$IconPath = Join-Path $PSScriptRoot "AppIcon.ico"
if (-not (Test-Path $IconPath)) {
	Write-Host ""
	Write-Host "==> No AppIcon.ico found — generating a placeholder icon..." -ForegroundColor Yellow
	# Create a minimal 1-frame 32x32 ICO using raw bytes (avoids requiring System.Drawing)
	# This is a valid ICO with a single 32x32 32-bpp white-on-purple "W" image.
	Add-Type -AssemblyName System.Drawing
	$bmp = New-Object System.Drawing.Bitmap(32, 32)
	$g   = [System.Drawing.Graphics]::FromImage($bmp)
	$g.Clear([System.Drawing.Color]::FromArgb(255, 123, 44, 191))
	$font  = New-Object System.Drawing.Font("Segoe UI", 18, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
	$sf    = New-Object System.Drawing.StringFormat
	$sf.Alignment     = [System.Drawing.StringAlignment]::Center
	$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
	$g.DrawString("W", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0,0,32,32)), $sf)
	$font.Dispose(); $g.Dispose()
	# Save as PNG first, then wrap in ICO container
	$pngStream = New-Object System.IO.MemoryStream
	$bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
	$bmp.Dispose()
	$pngBytes = $pngStream.ToArray(); $pngStream.Dispose()
	# ICO header: ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes) + PNG data
	$icoStream = New-Object System.IO.MemoryStream
	$writer    = New-Object System.IO.BinaryWriter($icoStream)
	$writer.Write([uint16]0)          # Reserved
	$writer.Write([uint16]1)          # Type = ICO
	$writer.Write([uint16]1)          # Image count
	# ICONDIRENTRY
	$writer.Write([byte]32)           # Width  (0 = 256)
	$writer.Write([byte]32)           # Height
	$writer.Write([byte]0)            # Color count
	$writer.Write([byte]0)            # Reserved
	$writer.Write([uint16]1)          # Planes
	$writer.Write([uint16]32)         # Bit count
	$writer.Write([uint32]$pngBytes.Length)  # Size of image data
	$writer.Write([uint32]22)         # Offset of image data (6 + 16 = 22)
	$writer.Write($pngBytes)
	$writer.Flush()
	[System.IO.File]::WriteAllBytes($IconPath, $icoStream.ToArray())
	$writer.Dispose(); $icoStream.Dispose()
	Write-Host "    Placeholder icon created: $IconPath"
	Write-Host "    Replace it with your real AppIcon.ico at any time." -ForegroundColor DarkYellow
}

# ── Step 4: Compile the installer with Inno Setup ────────────────────────────
Write-Host ""
Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan

# Pass values to the .iss script via /D defines (avoids modifying the file)
$IsccArgs = @(
	"`"$IssScript`"",
	"/DAppVersion=`"$InnoVersion4`"",
	"/DPublishDir=`"$PublishDir`"",
	"/DSourceDir=`"$PSScriptRoot`""
)

& $IsccExe @IsccArgs
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit code $LASTEXITCODE)." }

# ── Step 5: Report output ─────────────────────────────────────────────────────
$SetupExe = Get-ChildItem -Path $ArtifactsDir -Filter "WeddingMusicPlannerPro_Setup_*.exe" |
			Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host ""
Write-Host "══════════════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  BUILD COMPLETE" -ForegroundColor Green
Write-Host "══════════════════════════════════════════════════════" -ForegroundColor Green
if ($SetupExe) {
	$sizeMB = [Math]::Round($SetupExe.Length / 1MB, 1)
	Write-Host "  Installer : $($SetupExe.FullName)"
	Write-Host "  Size      : $sizeMB MB"
} else {
	Write-Host "  Installer created in: $ArtifactsDir"
}
Write-Host ""
Write-Host "  End users just double-click the Setup exe — no" -ForegroundColor Yellow
Write-Host "  prerequisites, no certificate steps required."   -ForegroundColor Yellow
Write-Host ""

# ── Cleanup ───────────────────────────────────────────────────────────────────
if (-not $KeepStage) {
	Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}
