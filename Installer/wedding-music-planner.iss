; Wedding Music Planner Pro - Inno Setup 6 script
; Built/compiled by build-setup.ps1 (do not compile by hand).
; Build script passes /D defines: AppVersion, SourceDir, PublishDir.

#ifndef AppVersion
  #define AppVersion "1.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir ".."
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish-win-x64"
#endif

#define AppName        "Wedding Music Planner Pro"
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
AppContact={#AppPublisher}

; Per-user install (no admin required) — matches modern app conventions.
; Installs to %LOCALAPPDATA%\Programs\Wedding Music Planner Pro
DefaultDirName={localappdata}\Programs\{#AppName}
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

; Architecture — we publish win-x64
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Misc
MinVersion=10.0.17763
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}

; Prevent installing while the app is running
AppMutex=WeddingMusicPlannerPro_SingleInstance

; Standard upgrade/reinstall behavior
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All files from the self-contained publish output.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";    Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Offer to launch the app after installation (checkbox, default ON).
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up any loose runtime files written next to the exe at run-time.
Type: filesandordirs; Name: "{app}\logs"

[Code]
// Ask the user whether to also delete app data (database, settings, cache)
// during uninstall. Only runs on a full uninstall, not an upgrade.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\WeddingMusicPlannerPro');
    if DirExists(DataDir) then
    begin
      if MsgBox(
        'Do you also want to remove your personal data (music library database, settings, cache)?'
        + #13#10#13#10
        + 'Click Yes to delete everything, or No to keep your data for a future reinstall.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
