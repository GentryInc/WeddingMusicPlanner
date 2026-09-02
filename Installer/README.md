# Installer — Windows Setup (one-click)

Produces a single **WeddingMusicPlannerPro_Setup_<version>.exe** for
**Wedding Music Planner Pro** using Inno Setup 6.

End users **just double-click the Setup exe** — no prerequisites,
no certificate steps, no manual commands required.

The app is self-contained (bundles .NET 8), so target machines do
**not** need .NET installed.

## Developer prerequisite

Only the **.NET 8 SDK** is needed on the build machine. Inno Setup 6 is
downloaded and installed silently by the script if not already present.

## Build (one command)

`powershell
powershell -ExecutionPolicy Bypass -File Installer\\build-setup.ps1
` 

Optional:

`powershell
Installer\\build-setup.ps1 -Version 1.2.0
` 

Output lands in rtifacts\\WeddingMusicPlannerPro_Setup_<version>.exe.

## What the Setup exe does for end users

1. Standard install wizard (Next -> Install -> Finish)
2. Installs to %ProgramFiles%\\Wedding Music Planner Pro\\`n3. Creates a Start Menu shortcut
4. Optional Desktop shortcut (checkbox)
5. Adds an Add/Remove Programs uninstaller entry
6. Offers to launch the app immediately after install

## Files

- wedding-music-planner.iss - Inno Setup script
- uild-setup.ps1           - one-click build script
- AppIcon.ico               - optional; auto-generated placeholder if absent

