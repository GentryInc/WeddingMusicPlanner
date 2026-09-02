; Wedding Music Planner Pro — Inno Setup 6 script
; Built/compiled by build-setup.ps1 (do not compile by hand unless paths below are correct).
; The build script replaces {#SourceDir} and {#AppVersion} before calling ISCC.

#define AppName        "Wedding Music Planner Pro"
#define AppVersion     "{#AppVersion}"
#define AppPublisher   "Gentry Inc"
#define AppURL         "https://github.com/GentryInc/WeddingMusicPlanner"
#define AppExeName     "WeddingMusicPlannerPro.Wpf.exe"
#define AppId          "{{A1F2E3D4-B5C6-7890-ABCD-EF1234567890}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

; Install to Program Files\Wedding Music Planner Pro (respects 32/64-bit correctly)
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Output
OutputDir={#SourceDir}\..\artifacts
OutputBaseFilename=WeddingMusicPlannerPro_Setup_{#AppVersion}
SetupIconFile={#SourceDir}\AppIcon.ico

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4

; UI / UX
WizardStyle=modern
WizardSizePercent=120
DisableWelcomePage=no
InfoBeforeFile=
InfoAfterFile=

; Architecture — we publish win-x64
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Misc
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All files from the self-contained publish output.
; {#SourceDir} is replaced by build-setup.ps1 with the absolute publish output path.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch the app after installation (checkbox, default ON).
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up any loose runtime files written next to the exe at run-time.
Type: filesandordirs; Name: "{app}\logs"
