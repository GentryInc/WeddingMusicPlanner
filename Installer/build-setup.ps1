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

	# Use a tiny C# helper to generate a proper ICO via System.Drawing.
	# .NET 8 SDK is available since we just used dotnet publish above.
	$iconGenDir = Join-Path $env:TEMP "wmp-icon-gen"
	New-Item -ItemType Directory -Path $iconGenDir -Force | Out-Null

	$csCode = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

class IconGen {
    static void Main(string[] args) {
        string outPath = args[0];

        // Render to a 32x32 bitmap
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(255, 123, 44, 191)); // purple
            using var font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            g.DrawString("W", font, Brushes.White, new RectangleF(0, 0, 32, 32), sf);
        }

        // Save as PNG into a memory stream
        using var pngMs = new MemoryStream();
        bmp.Save(pngMs, ImageFormat.Png);
        byte[] pngBytes = pngMs.ToArray();

        // Wrap PNG bytes in an ICO container
        using var fs = new FileStream(outPath, FileMode.Create);
        using var w = new BinaryWriter(fs);

        // ICONDIR (6 bytes)
        w.Write((ushort)0);              // Reserved
        w.Write((ushort)1);              // Type = ICO
        w.Write((ushort)1);              // Image count

        // ICONDIRENTRY (16 bytes)
        w.Write((byte)32);               // Width
        w.Write((byte)32);               // Height
        w.Write((byte)0);                // Color count
        w.Write((byte)0);                // Reserved
        w.Write((ushort)1);              // Planes
        w.Write((ushort)32);             // Bit count
        w.Write((uint)pngBytes.Length);  // Image data size
        w.Write((uint)22);               // Offset to image data (6 + 16)

        // PNG data
        w.Write(pngBytes);
        w.Flush();

        Console.WriteLine("Icon saved to " + outPath);
    }
}
'@

	$csFile = Join-Path $iconGenDir "IconGen.cs"
	[System.IO.File]::WriteAllText($csFile, $csCode)

	# Create a minimal .csproj for compilation
	$csprojContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
'@
	$csprojFile = Join-Path $iconGenDir "IconGen.csproj"
	[System.IO.File]::WriteAllText($csprojFile, $csprojContent)

	# Build and run the icon generator
	Push-Location $iconGenDir
	try {
		dotnet run --configuration Release -- $IconPath
		if ($LASTEXITCODE -ne 0) { throw "Icon generator failed (exit code $LASTEXITCODE)." }
	} finally {
		Pop-Location
	}

	Remove-Item $iconGenDir -Recurse -Force -ErrorAction SilentlyContinue

	if (-not (Test-Path $IconPath)) {
		throw "Icon generation did not produce a file at $IconPath"
	}

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
