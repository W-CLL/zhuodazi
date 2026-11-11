#ifndef AppVersion
#define AppVersion "3.5.3"
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
Filename: "{app}\app\ZhuoDazi.exe"; Description: "启动桌搭子"; WorkingDir: "{app}\app"; Flags: nowait; Check: ShouldStartApplication

[Code]
const
  AutoStartKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  AutoStartValueName = 'ZhuoDazi';
  InstallEventsUrl = 'https://in.desktoppet.online/api/device/install-events';
  CompletionTimestampPlaceholder = '__POST_UNINSTALL_UTC__';
  UninstallCanceledExitCode = 20;

type
  TSystemTime = record
    Year: Word;
    Month: Word;
    DayOfWeek: Word;
    Day: Word;
    Hour: Word;
    Minute: Word;
    Second: Word;
    Milliseconds: Word;
  end;

var
  UninstallCompletionPath: string;
  UninstallBatchPath: string;
  UninstallBatchSnapshot: AnsiString;
  UninstallBatchSnapshotLoaded: Boolean;

procedure GetSystemTime(var SystemTime: TSystemTime);
  external 'GetSystemTime@kernel32.dll stdcall';

function TwoDigits(Value: Word): string;
begin
  if Value < 10 then
    Result := '0' + IntToStr(Value)
  else
    Result := IntToStr(Value);
end;

function ThreeDigits(Value: Word): string;
begin
  if Value < 10 then
    Result := '00' + IntToStr(Value)
  else if Value < 100 then
    Result := '0' + IntToStr(Value)
  else
    Result := IntToStr(Value);
end;

function UtcTimestamp: string;
var
  Value: TSystemTime;
begin
  GetSystemTime(Value);
  Result := IntToStr(Value.Year) + '-' + TwoDigits(Value.Month) + '-' + TwoDigits(Value.Day) +
    'T' + TwoDigits(Value.Hour) + ':' + TwoDigits(Value.Minute) + ':' + TwoDigits(Value.Second) +
    '.' + ThreeDigits(Value.Milliseconds) + 'Z';
end;

function ContinueWithoutFeedback(const MessageText: string): Boolean;
begin
  Result := MsgBox(
    MessageText + #13#10 + #13#10 + '是否跳过反馈并继续卸载？',
    mbConfirmation,
    MB_YESNO) = IDYES;
end;

function InitializeUninstall: Boolean;
var
  HelperPath: string;
  ResultCode: Integer;
begin
  Result := True;
  UninstallCompletionPath := ExpandConstant('{tmp}\ZhuoDazi-uninstall-completion.json');
  UninstallBatchPath := ExpandConstant('{userappdata}\poko-desktop-pet\install-batch.json');
  UninstallBatchSnapshot := '';
  UninstallBatchSnapshotLoaded := False;
  DeleteFile(UninstallCompletionPath);
  HelperPath := ExpandConstant('{app}\app\ZhuoDazi.exe');

  if not FileExists(HelperPath) then
  begin
    Result := ContinueWithoutFeedback('卸载反馈窗口无法启动，因为程序文件已缺失。');
    Exit;
  end;

  if not Exec(
    HelperPath,
    '--uninstall-feedback "' + UninstallCompletionPath + '"',
    ExtractFileDir(HelperPath),
    SW_SHOWNORMAL,
    ewWaitUntilTerminated,
    ResultCode) then
  begin
    Result := ContinueWithoutFeedback('卸载反馈窗口无法启动。');
    Exit;
  end;

  if ResultCode = UninstallCanceledExitCode then
  begin
    DeleteFile(UninstallCompletionPath);
    Result := False;
  end
  else if ResultCode <> 0 then
    Result := ContinueWithoutFeedback('卸载反馈窗口未能正常完成。');

  if not Result then DeleteFile(UninstallCompletionPath);
end;

procedure CaptureInstallBatchMarker;
begin
  UninstallBatchSnapshot := '';
  UninstallBatchSnapshotLoaded := LoadStringFromFile(UninstallBatchPath, UninstallBatchSnapshot);
end;

procedure DeleteCapturedInstallBatchMarker;
var
  CurrentBatch: AnsiString;
begin
  if not UninstallBatchSnapshotLoaded then Exit;
  if not LoadStringFromFile(UninstallBatchPath, CurrentBatch) then Exit;
  if CurrentBatch = UninstallBatchSnapshot then DeleteFile(UninstallBatchPath);
end;

procedure TryReportUninstallCompleted;
var
  PayloadBytes: AnsiString;
  Payload: string;
  Request: Variant;
begin
  if (UninstallCompletionPath = '') or
     (not FileExists(UninstallCompletionPath)) or
     (not LoadStringFromFile(UninstallCompletionPath, PayloadBytes)) then
    Exit;

  Payload := String(PayloadBytes);
  if Pos(CompletionTimestampPlaceholder, Payload) = 0 then Exit;
  StringChangeEx(Payload, CompletionTimestampPlaceholder, UtcTimestamp, True);

  try
    Request := CreateOleObject('WinHttp.WinHttpRequest.5.1');
    Request.Option(6) := False; { WinHttpRequestOption_EnableRedirects }
    Request.SetTimeouts(1000, 2000, 2000, 2500);
    Request.Open('POST', InstallEventsUrl, False);
    Request.SetRequestHeader('Content-Type', 'application/json; charset=utf-8');
    Request.SetRequestHeader('User-Agent', 'ZhuoDazi-Uninstaller/{#AppVersion}');
    Request.SetRequestHeader('X-DeskPet-Platform', 'windows');
    Request.Send(Payload);
  except
    { Completion reporting is best effort and must never block file removal. }
  end;
end;

function ShouldStartApplication: Boolean;
var
  ArgumentIndex: Integer;
begin
  Result := True;
  { Automated package verification starts the app with isolated preview data. }
  for ArgumentIndex := 1 to ParamCount do
    if CompareText(ParamStr(ArgumentIndex), '/NOAPPSTART') = 0 then
    begin
      Result := False;
      Exit;
    end;
end;

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
  begin
    CaptureInstallBatchMarker;
    RegDeleteValue(HKCU, AutoStartKey, AutoStartValueName)
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    if not FileExists(ExpandConstant('{app}\app\ZhuoDazi.exe')) then
    begin
      TryReportUninstallCompleted;
      DeleteCapturedInstallBatchMarker;
    end;
    if UninstallCompletionPath <> '' then DeleteFile(UninstallCompletionPath);
  end;
end;
