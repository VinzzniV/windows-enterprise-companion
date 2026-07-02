; Per-user installer (ADR 0005): installs to the user's Programs folder
; WITHOUT administrator rights — consistent with the asInvoker elevation
; strategy (ADR 0002). Runtime data (%LOCALAPPDATA%\Wec: database, logs,
; WebView2 profile) is deliberately left untouched on uninstall.
;
; Build (version is injected by CI from Directory.Build.props):
;   ISCC.exe packaging\wec-installer.iss /DAppVersion=0.1.0
; Expects the self-contained publish output in <repo root>\publish.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{5A427778-C33F-457B-B960-DB186306C259}
AppName=Windows Enterprise Companion
AppVersion={#AppVersion}
AppPublisher=Vinzent Niederwieser
DefaultDirName={userpf}\Wec
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
OutputBaseFilename=wec-{#AppVersion}-setup
UninstallDisplayIcon={app}\Wec.Host.exe
WizardStyle=modern

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Windows Enterprise Companion"; Filename: "{app}\Wec.Host.exe"

[Run]
Filename: "{app}\Wec.Host.exe"; Description: "Launch Windows Enterprise Companion"; Flags: nowait postinstall skipifsilent
