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
; Do not silently reuse the registered legacy default directory.
UsePreviousAppDir=no
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

; Migration uses the genuine previous Inno uninstaller before installing
; to a different folder. Otherwise different uninstall logs with the same
; AppId leave duplicate program trees and stale Windows registrations.
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  LegacyDir, NewDir, RegisteredDir, PreviousUninstaller, Key: String;
  ExitCode: Integer;
begin
  Result := '';
  LegacyDir := ExpandConstant('{localappdata}\Programs\ARIEC60870');
  NewDir := ExpandConstant('{localappdata}\Programs\ArSee');
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' +
    '{#AppGuid}' + '}_is1';

  Log('ArSee migration preflight: checking previous HKCU uninstall registration');
  if not RegQueryStringValue(HKCU, Key, 'InstallLocation', RegisteredDir) then
  begin
    if DirExists(LegacyDir) then
      Result := 'Unregistered legacy ARIEC60870 program folder detected. ' +
        'No program files were changed. Please resolve the orphan folder first.';
    Log('ArSee migration preflight: InstallLocation registry missing; ' + Result);
    Exit;
  end;
  RegisteredDir := RemoveBackslashUnlessRoot(RegisteredDir);
  Log('ArSee migration previous: ' + RegisteredDir + ', expected legacy: ' + LegacyDir + ', wizard target: ' + WizardDirValue);
  if CompareText(RegisteredDir, LegacyDir) <> 0 then
  begin
    { Respect custom paths; do not orphan an existing installation. }
    if CompareText(RegisteredDir, WizardDirValue) <> 0 then
      Result := 'ArSee is already installed elsewhere. ' +
        'Choose its existing installation folder for an in-place update.';
    Log('ArSee migration: existing non-legacy installation path; ' + Result);
    Exit;
  end;

  { v3.6.17/18 in-app workers verify the original EXE path and would
    incorrectly roll back after a successful relocation. Reject only those
    worker-originated upgrade invocations BEFORE touching the old app. }
  if (Pos('/NOCLOSEAPPLICATIONS', UpperCase(GetCmdTail)) > 0) and
     (Pos('/NOFORCECLOSEAPPLICATIONS', UpperCase(GetCmdTail)) > 0) then
  begin
    Result := 'This legacy-folder upgrade must use the official installer directly. ' +
      'The older in-app updater cannot safely change the installation folder.';
    Exit;
  end;

  Log('ArSee migration: legacy install identified and in-place updater ruled out');
  if CompareText(WizardDirValue, NewDir) <> 0 then
  begin
    Result := 'This legacy installation can migrate only to the default Programs\ArSee directory.';
    Log('ArSee migration rejected wizard destination; ' + Result);
    Exit;
  end;
  { Never overwrite unexpected data at the destination. }
  if DirExists(NewDir) then
  begin
    Result := 'Programs\ArSee already contains files. The legacy installation was left intact. ' +
      'Inspect the destination before retrying.';
    Log('ArSee migration rejected destination collision; ' + Result);
    Exit;
  end;
  PreviousUninstaller := AddBackslash(LegacyDir) + 'unins000.exe';
  if (not FileExists(PreviousUninstaller)) or
     (not FileExists(AddBackslash(LegacyDir) + 'unins000.dat')) then
  begin
    Result := 'Legacy Inno uninstall records are missing. Migration stopped without changes.';
    Exit;
  end;
  { No wildcard or recursive deletion of user-owned data. The historical
    installer never removes the separate LocalAppData\ARIEC60870 workspace. }
  Log('ArSee migration: invoking registered legacy Inno uninstaller');
  if not Exec(PreviousUninstaller,
    '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE,
    ewWaitUntilTerminated, ExitCode) then
  begin
    Result := 'Could not launch the registered legacy uninstaller.';
    Exit;
  end;
  Log('ArSee migration: previous uninstaller returned code ' + IntToStr(ExitCode));
  if ExitCode <> 0 then
  begin
    Result := 'Legacy uninstall failed with exit code ' + IntToStr(ExitCode) + '.';
    Exit;
  end;
  if RegKeyExists(HKCU, Key) or
     FileExists(AddBackslash(LegacyDir) + 'ARIEC60870.exe') or
     FileExists(AddBackslash(LegacyDir) + 'ArSee.exe') then
  begin
    Result := 'Legacy uninstall was incomplete. The new installation did not start.';
    Log('ArSee migration: previous uninstall incomplete');
    Exit;
  end;
  if DirExists(LegacyDir) and (not RemoveDir(LegacyDir)) then
    Result := 'The old program directory contains unrecognized files. ' +
      'They were preserved. Review that directory before completing the migration.';
end;
