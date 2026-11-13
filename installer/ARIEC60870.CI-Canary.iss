; Copyright 2026 Ari Sulistiono
; SPDX-License-Identifier: Apache-2.0
; Synthetic Windows CI canary: isolates Inno runtime/runner bootstrapping
; from ArSee's large self-contained application payload.
#ifndef OutputRoot
  #define OutputRoot "..\artifacts\installer"
#endif
[Setup]
AppId={{871F3D9A-9B85-41D0-85E3-77104D83A229}
AppName=ArSee CI Installer Canary
AppVersion=1.0.0
DefaultDirName={localappdata}\Programs\ARIEC60870-CI-Canary
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=yes
CloseApplications=no
RestartApplications=no
Compression=none
; Diagnostic only: skip SetupLdr extraction to distinguish loader vs Setup runtime.
UseSetupLdr=no
OutputDir={#OutputRoot}
OutputBaseFilename=ARIEC60870-inno-ci-direct
[Files]
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "canary.txt"; Flags: ignoreversion
