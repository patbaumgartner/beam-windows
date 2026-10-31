; Beam installer — Inno Setup script.
; Build with ISCC.exe (Inno Setup 6.2+) or via the GitHub Actions release workflow.
; Usage:
;   ISCC.exe installer\Beam.iss
;   (expects publish output at src\Beam.Tray\bin\Release\net10.0-windows\publish)

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppName "Beam"
#define MyAppPublisher "patbaumgartner"
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
ChangesEnvironment=no

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

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Beam"; ValueData: """{app}\{#MyAppExeName}"" --hidden"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Beam now"; Flags: nowait postinstall skipifsilent
