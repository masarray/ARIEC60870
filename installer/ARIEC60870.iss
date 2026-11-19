; Copyright 2026 Ari Sulistiono
; SPDX-License-Identifier: Apache-2.0
; Compile on Windows with Inno Setup 6.3+.
#ifndef AppVersion
  #define AppVersion "3.6.9"
#endif
#ifndef SourceRoot
  #define SourceRoot "..\artifacts\passive-gui-publish"
#endif
#ifndef SourceExe
  #define SourceExe "ARIEC60870.Desktop.exe"
#endif
#ifndef OutputRoot
  #define OutputRoot "..\artifacts\installer"
#endif
#define AppGuid "8C785B50-1706-4D78-A4DF-7094E39BA3D4"

[Setup]
AppId={{{#AppGuid}}
AppName=ArSee
AppVersion={#AppVersion}
AppVerName=ArSee {#AppVersion}
AppPublisher=Ari Sulistiono
AppPublisherURL=https://github.com/masarray/arsee
AppSupportURL=https://github.com/masarray/arsee/issues
AppUpdatesURL=https://github.com/masarray/arsee/releases
AppCopyright=Copyright (C) 2026 Ari Sulistiono
DefaultDirName={localappdata}\Programs\ArSee
DefaultGroupName=ArSee
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousGroup=no
UsePreviousTasks=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\src\ARIEC60870.Desktop\Assets\Icons\ariec60870-app.ico
UninstallDisplayIcon={app}\ArSee.exe
UninstallDisplayName=ArSee
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
CloseApplicationsFilter=ARIEC60870.exe,ArSee.exe
RestartApplications=no
SetupLogging=yes
OutputDir={#OutputRoot}
OutputBaseFilename=ArSee-v{#AppVersion}-setup-win-x64

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceRoot}\{#SourceExe}"; DestDir: "{app}"; DestName: "ArSee.exe"; Flags: ignoreversion
Source: "{#SourceRoot}\*"; Excludes: "ARIEC60870.Desktop.exe,ARIEC60870.exe,ArSee.exe,*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; One-time upgrade housekeeping: remove ONLY known legacy program files and shortcuts.
; Preserve %LOCALAPPDATA%\ARIEC60870 settings, maps, captures and update journal.
[InstallDelete]
Type: files; Name: "{app}\ARIEC60870.exe"
Type: files; Name: "{group}\ARIEC60870.lnk"
Type: files; Name: "{userprograms}\ARIEC60870\ARIEC60870.lnk"
Type: files; Name: "{userprograms}\ARIEC60870\ArSee.lnk"
Type: dirifempty; Name: "{userprograms}\ARIEC60870"
Type: files; Name: "{userdesktop}\ARIEC60870.lnk"

[Icons]
Name: "{group}\ArSee"; Filename: "{app}\ArSee.exe"
Name: "{autodesktop}\ArSee"; Filename: "{app}\ArSee.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ArSee.exe"; Description: "Launch ArSee"; Flags: nowait postinstall skipifsilent

; Intentionally NO [UninstallDelete] for %LOCALAPPDATA%\ARIEC60870
; (settings, Signal Lists, capture history and updater state live outside {app}).
; Windows Restart Manager requests normal shutdown; no force-kill by default.
