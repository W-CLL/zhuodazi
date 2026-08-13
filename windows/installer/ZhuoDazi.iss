#ifndef AppVersion
#define AppVersion "3.1.2"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{B8FFDA14-C49B-4C72-91E5-A88A92747D26}
AppName=桌搭子
AppVersion={#AppVersion}
AppVerName=桌搭子 {#AppVersion}
AppPublisher=桌搭子
DefaultDirName={localappdata}\Programs\ZhuoDazi
DefaultGroupName=桌搭子
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ZhuoDazi-Desktop-Pet-{#AppVersion}
SetupIconFile=..\build\app-icon.ico
UninstallDisplayIcon={app}\app\ZhuoDazi.exe
UninstallFilesDir={app}\uninstall
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=yes
CloseApplicationsFilter=ZhuoDazi.exe
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName=桌搭子
VersionInfoDescription=桌搭子桌面宠物

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："

[InstallDelete]
Type: filesandordirs; Name: "{app}\app"
Type: files; Name: "{app}\ZhuoDazi.exe"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\createdump.exe"
Type: files; Name: "{app}\ZhuoDazi.deps.json"
Type: files; Name: "{app}\ZhuoDazi.runtimeconfig.json"
Type: filesandordirs; Name: "{app}\assets"
Type: filesandordirs; Name: "{app}\resources"
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\de"
Type: filesandordirs; Name: "{app}\es"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\pt-BR"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\zh-Hans"
Type: filesandordirs; Name: "{app}\zh-Hant"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\桌搭子"; Filename: "{app}\app\ZhuoDazi.exe"; WorkingDir: "{app}\app"
Name: "{autodesktop}\桌搭子"; Filename: "{app}\app\ZhuoDazi.exe"; WorkingDir: "{app}\app"; Tasks: desktopicon

[Run]
Filename: "{app}\app\ZhuoDazi.exe"; Description: "启动桌搭子"; WorkingDir: "{app}\app"; Flags: nowait

[Code]
const
  AutoStartKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  AutoStartValueName = 'ZhuoDazi';

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExistingCommand: string;
begin
  if (CurStep = ssPostInstall) and
     RegQueryStringValue(HKCU, AutoStartKey, AutoStartValueName, ExistingCommand) then
  begin
    RegWriteStringValue(
      HKCU,
      AutoStartKey,
      AutoStartValueName,
      '"' + ExpandConstant('{app}\app\ZhuoDazi.exe') + '"');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, AutoStartKey, AutoStartValueName);
end;
