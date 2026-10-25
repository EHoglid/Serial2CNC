#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained desktop publish directory
#endif

[Setup]
AppId={{E125BC4B-2768-4396-B0BF-18D8BB5AE16C}
AppName=Serial2CNC
AppVersion={#AppVersion}
AppPublisher=Serial2CNC
DefaultDirName={localappdata}\Programs\Serial2CNC
DefaultGroupName=Serial2CNC
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=Serial2CNC-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Serial2CNC.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Serial2CNC"; Filename: "{app}\Serial2CNC.exe"
Name: "{autodesktop}\Serial2CNC"; Filename: "{app}\Serial2CNC.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Serial2CNC.exe"; Description: "Launch Serial2CNC"; Flags: nowait postinstall skipifsilent
