<#
.SYNOPSIS
	One-click installer builder for Wedding Music Planner Pro.

.DESCRIPTION
	Publishes the WPF app as a self-contained win-x64 build, then compiles a
	standard Windows Setup.exe using Inno Setup 6.

	If Inno Setup 6 is not installed it is downloaded and installed silently.

	Run from anywhere:
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

# -- Paths -------------------------------------------------------------------
$RepoRoot     = Split-Path -Parent $PSScriptRoot
$Project      = Join-Path $RepoRoot "WeddingMusicPlannerPro.Wpf\WeddingMusicPlannerPro.Wpf.csproj"
$IssScript    = Join-Path $PSScriptRoot "wedding-music-planner.iss"
$PublishDir   = Join-Path $RepoRoot "artifacts\publish-win-x64"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$Rid          = "win-x64"

# Version used by Inno Setup must be numeric
$InnoVersion = ($Version -replace '-.*$','').Trim()
if ($InnoVersion -notmatch '^\d+(\.\d+){0,3}$') {
	throw "Version '$Version' could not be reduced to a numeric form for Inno Setup."
}
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

# -- Step 1: Ensure Inno Setup 6 is available ---------------------------------
$IsccExe = $null
$CandidatePaths = @(
	"${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
	"${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
foreach ($p in $CandidatePaths) {
	if (Test-Path $p) { $IsccExe = $p; break }
}

if (-not $IsccExe) {
	Write-Host "==> Inno Setup 6 not found - downloading and installing silently..." -ForegroundColor Yellow
	$InnoInstaller = Join-Path $env:TEMP "innosetup-6.7.3.exe"
	# Inno Setup 6 is hosted on GitHub releases (files.jrsoftware.org no longer hosts 6.x directly)
	$DownloadUrls = @(
		"https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe",
		"https://github.com/jrsoftware/issrc/releases/download/is-6_7_2/innosetup-6.7.2.exe",
		"https://github.com/jrsoftware/issrc/releases/download/is-6_7_1/innosetup-6.7.1.exe"
	)
	$downloaded = $false
	foreach ($url in $DownloadUrls) {
		try {
			Write-Host "    Trying $url ..."
			Invoke-WebRequest -Uri $url -OutFile $InnoInstaller -UseBasicParsing
			$downloaded = $true
			break
		} catch {
			Write-Host "    Failed: $($_.Exception.Message)" -ForegroundColor DarkYellow
		}
	}
	if (-not $downloaded) {
		throw "Could not download Inno Setup 6 automatically.`nInstall it manually: winget install --id JRSoftware.InnoSetup -e"
	}
	Write-Host "    Installing Inno Setup silently ..."
	Start-Process -FilePath $InnoInstaller -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" -Wait
	Remove-Item $InnoInstaller -Force -ErrorAction SilentlyContinue
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

# -- Step 2: dotnet publish (self-contained, win-x64) -------------------------
Write-Host ""
Write-Host "==> Publishing self-contained $Rid build..." -ForegroundColor Cyan

if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir   -Force | Out-Null
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

dotnet publish $Project --configuration $Configuration --runtime $Rid --self-contained true -p:PublishSingleFile=false -p:Version=$Version --output $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }
Write-Host "    Publish succeeded -> $PublishDir" -ForegroundColor Green

# -- Step 3: Generate a placeholder icon if none is present -------------------
$IconPath = Join-Path $PSScriptRoot "AppIcon.ico"
if (-not (Test-Path $IconPath)) {
	Write-Host ""
	Write-Host "==> No AppIcon.ico found - generating a placeholder icon..." -ForegroundColor Yellow

	# Build a minimal valid ICO: 32x32 32bpp, purple with white "W"
	$imgSize = 32
	$pixels  = [byte[]]::new($imgSize * $imgSize * 4)

	# Fill purple (BGRA: B=191, G=44, R=123, A=255)
	for ($i = 0; $i -lt $pixels.Length; $i += 4) {
		$pixels[$i]   = 191
		$pixels[$i+1] = 44
		$pixels[$i+2] = 123
		$pixels[$i+3] = 255
	}

	# Draw a crude white "W"
	$w  = $imgSize
	$wh = @(255,255,255,255)

	# Left stroke
	for ($y = 8; $y -le 24; $y++) {
		$x1 = 8 + [int](($y - 8) * 0.3)
		foreach ($x in @($x1, ($x1+1))) {
			$off = ($y * $w + $x) * 4
			for ($c = 0; $c -lt 4; $c++) { $pixels[$off+$c] = $wh[$c] }
		}
	}
	# First valley
	for ($y = 20; $y -le 24; $y++) {
		$x1 = 14 + [int](($y - 20) * 0.75)
		foreach ($x in @($x1, ($x1+1))) {
			$off = ($y * $w + $x) * 4
			for ($c = 0; $c -lt 4; $c++) { $pixels[$off+$c] = $wh[$c] }
		}
	}
	# Peak
	for ($y = 16; $y -le 24; $y++) {
		$x1 = 16 - [int](($y - 16) * 0.25)
		$x2 = 16 + [int](($y - 16) * 0.25)
		foreach ($x in @($x1, ($x1+1), $x2, ($x2+1))) {
			$off = ($y * $w + $x) * 4
			for ($c = 0; $c -lt 4; $c++) { $pixels[$off+$c] = $wh[$c] }
		}
	}
	# Right stroke
	for ($y = 8; $y -le 24; $y++) {
		$x1 = 22 - [int](($y - 8) * 0.3)
		foreach ($x in @($x1, ($x1+1))) {
			$off = ($y * $w + $x) * 4
			for ($c = 0; $c -lt 4; $c++) { $pixels[$off+$c] = $wh[$c] }
		}
	}

	# ICO container: ICONDIR + ICONDIRENTRY + BITMAPINFOHEADER + XOR mask + AND mask
	$bmpHeaderSize = 40
	$andMaskSize   = [math]::Ceiling($imgSize / 32) * 4 * $imgSize
	$bmpDataSize   = $pixels.Length + $andMaskSize

	$icoStream = New-Object System.IO.MemoryStream
	$writer    = New-Object System.IO.BinaryWriter($icoStream)

	# ICONDIR (6 bytes)
	$writer.Write([uint16]0)
	$writer.Write([uint16]1)
	$writer.Write([uint16]1)

	# ICONDIRENTRY (16 bytes)
	$writer.Write([byte]$imgSize)
	$writer.Write([byte]$imgSize)
	$writer.Write([byte]0)
	$writer.Write([byte]0)
	$writer.Write([uint16]1)
	$writer.Write([uint16]32)
	$writer.Write([uint32]($bmpHeaderSize + $bmpDataSize))
	$writer.Write([uint32]22)

	# BITMAPINFOHEADER (40 bytes)
	$writer.Write([uint32]$bmpHeaderSize)
	$writer.Write([int32]$imgSize)
	$writer.Write([int32]($imgSize * 2))
	$writer.Write([uint16]1)
	$writer.Write([uint16]32)
	$writer.Write([uint32]0)
	$writer.Write([uint32]$bmpDataSize)
	$writer.Write([int32]0)
	$writer.Write([int32]0)
	$writer.Write([uint32]0)
	$writer.Write([uint32]0)

	# XOR mask (bottom-up pixel rows)
	for ($y = ($imgSize - 1); $y -ge 0; $y--) {
		for ($x = 0; $x -lt $imgSize; $x++) {
			$off = ($y * $imgSize + $x) * 4
			$writer.Write([byte[]]@($pixels[$off], $pixels[$off+1], $pixels[$off+2], $pixels[$off+3]))
		}
	}

	# AND mask (all zeros = fully opaque)
	$writer.Write([byte[]]::new($andMaskSize))
	$writer.Flush()
	[System.IO.File]::WriteAllBytes($IconPath, $icoStream.ToArray())
	$writer.Dispose()
	$icoStream.Dispose()

	Write-Host "    Placeholder icon created: $IconPath"
	Write-Host "    Replace it with your real AppIcon.ico at any time." -ForegroundColor DarkYellow
}

# -- Step 4: Compile the installer with Inno Setup ----------------------------
Write-Host ""
Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan

$IsccArgs = @(
	"`"$IssScript`"",
	"/DAppVersion=`"$InnoVersion4`"",
	"/DPublishDir=`"$PublishDir`"",
	"/DSourceDir=`"$PSScriptRoot`""
)

& $IsccExe @IsccArgs
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit code $LASTEXITCODE)." }

# -- Step 5: Report output -----------------------------------------------------
$SetupExe = Get-ChildItem -Path $ArtifactsDir -Filter "WeddingMusicPlannerPro_Setup_*.exe" |
			Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host ""
Write-Host "======================================================" -ForegroundColor Green
Write-Host "  BUILD COMPLETE" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
if ($SetupExe) {
	$sizeMB = [Math]::Round($SetupExe.Length / 1MB, 1)
	Write-Host "  Installer : $($SetupExe.FullName)"
	Write-Host "  Size      : $sizeMB MB"
} else {
	Write-Host "  Installer created in: $ArtifactsDir"
}
Write-Host ""
Write-Host "  End users just double-click the Setup exe." -ForegroundColor Yellow
Write-Host ""

# -- Cleanup -------------------------------------------------------------------
if (-not $KeepStage) {
	Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
}
