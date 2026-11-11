#if Ver < EncodeVer(6, 7, 1)
  #error Inno Setup 6.7.1 or newer is required to replace the uninstaller shipped with 3.5.7.
#endif

#ifndef AppVersion
#define AppVersion "3.5.8"
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
  UninstallMutexHandle: THandle;

function CreateMutexForUninstall(Attributes: Integer; InitialOwner: Boolean; Name: string): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function CloseUninstallHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

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

function JsonStringField(const Json, Key: string): string;
var
  StartIndex, EndIndex: Integer;
  Remaining: string;
begin
  Result := '';
  StartIndex := Pos('"' + Key + '":"', Json);
  if StartIndex = 0 then Exit;
  Remaining := Copy(Json, StartIndex + Length(Key) + 4, Length(Json));
  EndIndex := Pos('"', Remaining);
  if EndIndex > 0 then Result := Copy(Remaining, 1, EndIndex - 1);
end;

procedure TryReportUninstallCompleted;
var
  PayloadBytes: AnsiString;
  Payload, EventId, ResponseText: string;
  Request: Variant;
  Attempt, StatusCode: Integer;
  Acknowledged, PermanentFailure: Boolean;
begin
  if (UninstallCompletionPath = '') or
     (not FileExists(UninstallCompletionPath)) or
     (not LoadStringFromFile(UninstallCompletionPath, PayloadBytes)) then
  begin
    Log('Uninstall completion unavailable: no receipt (feedback may have been offline).');
    Exit;
  end;

  Payload := String(PayloadBytes);
  if Pos(CompletionTimestampPlaceholder, Payload) = 0 then Exit;
  StringChangeEx(Payload, CompletionTimestampPlaceholder, UtcTimestamp, True);
  EventId := JsonStringField(Payload, 'eventId');
  if Length(EventId) <> 36 then Exit;
  { Reuse the exact event and timestamp: retries must not duplicate an uninstall. }
  for Attempt := 1 to 3 do
  begin
    StatusCode := 0;
    Acknowledged := False;
    PermanentFailure := False;
    try
      Request := CreateOleObject('WinHttp.WinHttpRequest.5.1');
      Request.Option(6) := False; { WinHttpRequestOption_EnableRedirects }
      Request.SetProxy(1); { Direct, matching the desktop client's transport. }
      Request.SetTimeouts(1000, 1500, 1500, 2000);
      Request.Open('POST', InstallEventsUrl, False);
      Request.SetRequestHeader('Content-Type', 'application/json; charset=utf-8');
      Request.SetRequestHeader('User-Agent', 'ZhuoDazi-Uninstaller/{#AppVersion}');
      Request.SetRequestHeader('X-DeskPet-Platform', 'windows');
      Request.Send(Payload);
      StatusCode := Request.Status;
      ResponseText := Request.ResponseText;
      if (StatusCode >= 200) and (StatusCode < 300) and (Length(ResponseText) <= 32768) and
         (Pos('"ok":true', ResponseText) > 0) and
         (JsonStringField(ResponseText, 'eventId') = EventId) and
         (Pos('"ignoredAsStale":false', ResponseText) > 0) and
         (JsonStringField(ResponseText, 'deviceState') = 'uninstall_reported') then
      begin
        Acknowledged := True;
      end
      else Log('Uninstall completion not acknowledged; HTTP ' + IntToStr(StatusCode) + '.');
      PermanentFailure := (StatusCode >= 400) and (StatusCode < 500) and
         (StatusCode <> 408) and (StatusCode <> 429);
    except
      Log('Uninstall completion request failed; bounded retry follows.');
    end;
    { Keep loop control outside COM exception handling in Pascal Script. }
    if Acknowledged then
    begin
      Log('Uninstall completion acknowledged.');
      Exit;
    end;
    if PermanentFailure then Break;
    if Attempt < 3 then Sleep(250 * Attempt);
  end;
  Log('Files were removed, but the server has not confirmed the uninstall.');
end;

procedure StopInstalledApplication;
var
  HelperPath: string;
  ResultCode: Integer;
begin
  { The fence exists only during confirmed removal and is released on every exit. }
  UninstallMutexHandle := CreateMutexForUninstall(0, False, 'Local\ZhuoDazi.Uninstall.InProgress');
  if UninstallMutexHandle = 0 then
    RaiseException('无法准备卸载，请关闭桌搭子后重试。');
  HelperPath := ExpandConstant('{app}\app\ZhuoDazi.exe');
  { An older root-level executable may not understand the new helper switch. }
  if not FileExists(HelperPath) and FileExists(ExpandConstant('{app}\ZhuoDazi.exe')) then
    RaiseException('程序文件不完整，请重新安装桌搭子后再卸载。');
  if not FileExists(HelperPath) then Exit;
  if not Exec(HelperPath, '--prepare-uninstall', ExtractFileDir(HelperPath),
      SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('无法关闭桌搭子，请退出桌搭子后重新卸载。');
  if ResultCode <> 0 then
    RaiseException('桌搭子仍在运行，卸载已停止。请关闭桌搭子后重试。');
end;

procedure DeinitializeUninstall;
begin
  if UninstallMutexHandle <> 0 then
  begin
    CloseUninstallHandle(UninstallMutexHandle);
    UninstallMutexHandle := 0;
  end;
  if UninstallCompletionPath <> '' then DeleteFile(UninstallCompletionPath);
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
    StopInstalledApplication;
    CaptureInstallBatchMarker;
    RegDeleteValue(HKCU, AutoStartKey, AutoStartValueName)
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    if not FileExists(ExpandConstant('{app}\app\ZhuoDazi.exe')) and
       not FileExists(ExpandConstant('{app}\ZhuoDazi.exe')) then
    begin
      TryReportUninstallCompleted;
      DeleteCapturedInstallBatchMarker;
    end
    else Log('Uninstall incomplete: application executable remains; completion was not reported.');
    if UninstallCompletionPath <> '' then DeleteFile(UninstallCompletionPath);
  end;
end;
