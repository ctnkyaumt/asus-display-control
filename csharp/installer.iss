; Inno Setup script — installer for the C# ASUS Display Control app.
;
; Compile:  iscc csharp\installer.iss           (run csharp\build.ps1 first)
; Input:    csharp\publish\   (self-contained one-folder output)
; Output:   csharp\dist\ASUS-Display-Control-Setup.exe
;
; Version override from CI:  iscc /DAppVersion=1.2.3 csharp\installer.iss

#ifndef AppVersion
  #define AppVersion "2.2.2"
#endif
#define AppName "ASUS Display Control"
#define AppPublisher "ctnkyaumt"
#define AppExeName "ASUS-Display-Control.exe"
#define AppUrl "https://github.com/ctnkyaumt/asus-display-control"

[Setup]
AppId={{8B4E2A16-3C7D-4F9A-B1E5-6D2C8A9F0B34}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=dist
OutputBaseFilename=ASUS-Display-Control-Setup
SetupIconFile=icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon"; Description: "Start {#AppName} automatically when Windows starts (in the tray)"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Optional "start with Windows" — same HKCU Run value the in-app toggle manages.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "ASUSDisplayControl"; ValueData: """{app}\{#AppExeName}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
{ Uninstalling used to delete %APPDATA%\ASUSDisplayControl outright, taking the user's
  presets, schedule and per-app rules with it — including on a reinstall. Ask instead.
  A silent uninstall keeps the data (SuppressibleMsgBox falls back to the IDNO default). }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\ASUSDisplayControl');
    if DirExists(DataDir) then
      if SuppressibleMsgBox('Also delete your saved presets, schedule, per-app rules and settings?'
                            + #13#10 + #13#10 + DataDir
                            + #13#10 + #13#10 + 'Choose No to keep them for a future install.',
                            mbConfirmation, MB_YESNO, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
