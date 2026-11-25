; Beam installer — Inno Setup script.
; Build with ISCC.exe (Inno Setup 6.3+) or via the GitHub Actions release workflow.
; Usage:
;   ISCC.exe installer\Beam.iss
;   (expects publish output at src\Beam.Tray\bin\Release\net10.0-windows\publish)

#ifndef MyAppVersion
  #define MyAppVersion "0.3.1"
#endif
#define MyAppName "Beam"
#define MyAppPublisher "Patrick Baumgartner"
#define MyAppPublisherUrl "https://github.com/patbaumgartner/beam-windows"
#define MyAppExeName "Beam.exe"
#define MyAppDir "..\src\Beam.Tray\bin\Release\net10.0-windows\publish"
#define MyCliDir "..\src\Beam.Cli\bin\Release\net10.0-windows\publish"
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{8F3A5C1D-2B0E-4F9D-A9C4-0B11D0F0C000}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherUrl}
AppSupportURL={#MyAppPublisherUrl}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=BeamSetup-{#MyAppVersion}
SetupIconFile=..\src\Beam.Tray\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableDirPage=auto
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Components]
Name: "tray"; Description: "Beam tray application"; Types: full custom; Flags: fixed
Name: "cli"; Description: "Beam command-line interface (Beam.Cli.exe)"; Types: full custom

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "autostart"; Description: "Launch Beam at &sign-in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Tray app (core).
Source: "{#MyAppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: tray
; CLI — a thin, self-contained exe (component, on by default).
Source: "{#MyCliDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: cli
Source: "beam.cmd"; DestDir: "{app}"; Flags: ignoreversion; Components: cli
; Required license and asset attribution notices.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\assets\ATTRIBUTION.md"; DestDir: "{app}"; DestName: "THIRD-PARTY-NOTICES.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Beam"; ValueData: """{app}\{#MyAppExeName}"" --hidden"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Beam now"; Flags: nowait postinstall skipifsilent

[Code]
function ContainsPathEntry(const PathList, Entry: string): Boolean;
begin
  Result := Pos(';' + Lowercase(Entry) + ';', ';' + Lowercase(PathList) + ';') > 0;
end;

procedure AddBeamToUserPath;
var
  CurrentPath, BeamDir: string;
begin
  BeamDir := ExpandConstant('{app}');
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
    CurrentPath := '';
  if ContainsPathEntry(CurrentPath, BeamDir) then
    exit;
  if CurrentPath <> '' then
    if CurrentPath[Length(CurrentPath)] <> ';' then
      CurrentPath := CurrentPath + ';';
  CurrentPath := CurrentPath + BeamDir;
  RegWriteExpandStringValue(HKCU, 'Environment', 'Path', CurrentPath);
end;

procedure RemoveBeamFromUserPath;
var
  CurrentPath, BeamDir, WrappedPath, Token: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKCU, 'Environment', 'Path', CurrentPath) then
    exit;
  BeamDir := ExpandConstant('{app}');
  WrappedPath := ';' + Lowercase(CurrentPath) + ';';
  Token := ';' + Lowercase(BeamDir) + ';';
  P := Pos(Token, WrappedPath);
  if P = 0 then
    exit;
  Delete(CurrentPath, P, Length(Token) - 1);
  if Trim(CurrentPath) = '' then
    RegDeleteValue(HKCU, 'Environment', 'Path')
  else
    RegWriteExpandStringValue(HKCU, 'Environment', 'Path', CurrentPath);
end;

procedure RemoveBeamRunEntryForThisInstall;
var
  Value, QuotedCommand, BareCommand: string;
begin
  if not RegQueryStringValue(HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Run', 'Beam', Value) then
    exit;
  BareCommand := ExpandConstant('{app}\{#MyAppExeName}');
  QuotedCommand := '"' + BareCommand + '" --hidden';
  if (CompareText(Value, BareCommand) = 0) or (CompareText(Value, QuotedCommand) = 0) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Beam');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsComponentSelected('cli') then
    AddBeamToUserPath;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RemoveBeamFromUserPath;
    RemoveBeamRunEntryForThisInstall;
  end;
end;
