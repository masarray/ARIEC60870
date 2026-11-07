; Copyright 2026 Ari Sulistiono
; SPDX-License-Identifier: Apache-2.0
; Compile on Windows with Inno Setup 6.3+.
#ifndef AppVersion
  #define AppVersion "3.6.7"
#endif
#ifndef SourceRoot
  #define SourceRoot "..\artifacts\passive-gui-publish"
#endif
#ifndef OutputRoot
  #define OutputRoot "..\artifacts\installer"
#endif
#define AppGuid "8C785B50-1706-4D78-A4DF-7094E39BA3D4"

[Setup]
AppId={{{#AppGuid}}
AppName=ARIEC60870 Evidence Analyzer
AppVersion={#AppVersion}
AppVerName=ARIEC60870 {#AppVersion}
AppPublisher=Ari Sulistiono
AppPublisherURL=https://github.com/masarray/ARIEC60870
AppSupportURL=https://github.com/masarray/ARIEC60870/issues
AppUpdatesURL=https://github.com/masarray/ARIEC60870/releases
AppCopyright=Copyright (C) 2026 Ari Sulistiono
DefaultDirName={localappdata}\Programs\ARIEC60870
DefaultGroupName=ARIEC60870
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\src\ARIEC60870.Desktop\Assets\Icons\ariec60870-app.ico
UninstallDisplayIcon={app}\ARIEC60870.exe
UninstallDisplayName=ARIEC60870 Evidence Analyzer
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=ARIEC60870.exe
RestartApplications=no
SetupLogging=yes
OutputDir={#OutputRoot}
OutputBaseFilename=ARIEC60870-v{#AppVersion}-setup-win-x64

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceRoot}\ARIEC60870.Desktop.exe"; DestDir: "{app}"; DestName: "ARIEC60870.exe"; Flags: ignoreversion
Source: "{#SourceRoot}\*"; Excludes: "ARIEC60870.Desktop.exe,*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ARIEC60870"; Filename: "{app}\ARIEC60870.exe"
Name: "{autodesktop}\ARIEC60870"; Filename: "{app}\ARIEC60870.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ARIEC60870.exe"; Description: "Launch ARIEC60870"; Flags: nowait postinstall skipifsilent

; Intentionally NO [UninstallDelete] for %LOCALAPPDATA%\ARIEC60870
; (settings, Signal Lists, capture history and updater state live outside {app}).
; Windows Restart Manager requests normal shutdown; no force-kill by default.
