; WatchMe Windows installer (Inno Setup 6).
; Build: publish the app to ../publish, then ISCC.exe setup/WatchMe.iss

#define MyAppName "WatchMe"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "WatchMe"
#define MyAppExeName "WatchMe.exe"
#define MyAppURL "https://github.com/bensoymallari-design/WatchoutDesktopClient"

[Setup]
AppId={{8F3C2A1B-9D4E-4C7A-B6E2-1A0D5C8F3E21}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=WatchMe-Setup
SetupIconFile=..\src\Watchout.Desktop\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WatchMe"; Flags: nowait postinstall skipifsilent

[Code]
function ChangeDisplaySettingsA(lpDevMode: Longint; dwFlags: DWORD): Longint;
  external 'ChangeDisplaySettingsA@user32.dll stdcall';
function SetDisplayConfig(pathCount: DWORD; paths: Longint; modeCount: DWORD; modes: Longint; flags: DWORD): Longint;
  external 'SetDisplayConfig@user32.dll stdcall';

procedure KillWatchMe;
var
  ResultCode: Integer;
begin
  { Output is a topmost black window on the wall. A hung DXVA/DXGI Present
    ignores WM_CLOSE, so uninstall would leave that screen black. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RestoreDisplays;
var
  ResultCode: Integer;
begin
  { DXGI exclusive / killed Present can leave HDMI at a mode Win+P will not fix. }
  ChangeDisplaySettingsA(0, 0);
  SetDisplayConfig(0, 0, 0, 0, $84);
  Exec(ExpandConstant('{sys}\DisplaySwitch.exe'), '/extend', '', SW_HIDE, ewNoWait, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  KillWatchMe;
  RestoreDisplays;
  Result := True;
end;

function InitializeUninstall(): Boolean;
begin
  KillWatchMe;
  RestoreDisplays;
  Result := True;
end;

