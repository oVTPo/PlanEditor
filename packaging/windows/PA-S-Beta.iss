#define MyAppName "PA-S"
#define MyAppPublisher "PA-S"
#define MyAppExeName "PA-S.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-beta.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\build\beta\windows\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist\beta"
#endif

[Setup]
AppId={{D03B2AA7-177D-45AE-8EF0-A6F7CF3E11C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\PA-S
DefaultGroupName=PA-S
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=PA-S-{#MyAppVersion}-Windows-x64-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=app.ico
UninstallDisplayIcon={app}\PA-S.exe
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=0.1.0.0
VersionInfoCompany=PA-S
VersionInfoDescription=PA-S Beta Installer
VersionInfoProductName=PA-S
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "pas-project.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\PA-S"; Filename: "{app}\PA-S.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall PA-S"; Filename: "{uninstallexe}"
Name: "{userdesktop}\PA-S"; Filename: "{app}\PA-S.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng PA-S ngoài Desktop"; GroupDescription: "Biểu tượng bổ sung:"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Classes\.pas"; ValueType: string; ValueName: ""; ValueData: "PAS.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.pas"; ValueType: string; ValueName: "Content Type"; ValueData: "application/x-pas-project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\PAS.Project"; ValueType: string; ValueName: ""; ValueData: "PA-S Project"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\PAS.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\pas-project.ico"",0"
Root: HKCU; Subkey: "Software\Classes\PAS.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\PA-S.exe"" ""%1"""

[Run]
Filename: "{app}\PA-S.exe"; Description: "Khởi động PA-S"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C ""taskkill /IM PA-S.exe /F >nul 2>&1"""; Flags: runhidden; RunOnceId: "KillPAS"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Cache"
Type: filesandordirs; Name: "{app}\Logs"
