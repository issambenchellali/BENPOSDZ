; ============================================================
; BENPOSDZ.iss - Windows installer (Inno Setup)
;
; Usage:
;   ISCC.exe installer\BENPOSDZ.iss /DAppVersion=1.1.0 /DOutputDir=releases
;
; Prerequisite: publish folder must be ready first:
;   scripts\publish.ps1 -Version 1.1.0   (or manual publish)
; ============================================================

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef OutputDir
  #define OutputDir "..\releases"
#endif

#define AppName "BEN POS"
#define AppPublisher "BENPOSDZ"
#define AppExeName "BENPOSDZ.exe"
#define SourceDir "..\BENPOSDZ\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{8B6A4C32-8F21-4C1E-9A52-5E1A4D0B7F01}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/issambenchellali/BENPOSDZ
DefaultDirName={autopf}\BENPOSDZ
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=BENPOSDZ_Setup_{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
SetupLogging=yes
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; Excludes: "*.pdb,BENPOSUpdater.exe,BENPOSDZ.exe.WebView2\EBWebView"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
#ifexist SourceDir + "\\BENPOSUpdater.exe"
Source: "{#SourceDir}\BENPOSUpdater.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\*.pdb"

[Code]
// Keep user data (SQLite DB in AppData) intact during uninstall/upgrade.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Nothing to do: data lives in AppData which the installer never touches.
end;
