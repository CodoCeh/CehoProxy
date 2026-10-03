#define AppName "CehoProxy"
#define AppVersion "1.2.154"
#define AppPublisher "КодоЦех"
#define AppUrl "https://codoceh.ru"
#define RepoUrl "https://github.com/CodoCeh/CehoProxy"

[Setup]
AppId={{6E2C3F41-8B7A-4E2D-9C1F-2A5D7B0E9C33}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} — установка
VersionInfoCompany={#AppPublisher}
AppSupportURL={#RepoUrl}
AppUpdatesURL={#RepoUrl}
DefaultDirName={commonappdata}\CehoProxy
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
OutputBaseFilename=CehoProxy-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=no
WizardStyle=modern
DisableWelcomePage=no
UsePreviousTasks=no
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\assets\cehoproxy.ico
WizardImageFile=..\assets\wizard-164.bmp,..\assets\wizard-328.bmp
WizardSmallImageFile=..\assets\wizard-small-55.bmp,..\assets\wizard-small-110.bmp
WizardImageBackColor=$121510
WizardImageAlphaFormat=none
UninstallDisplayIcon={app}\cehoproxy.exe
UninstallDisplayName={#AppName}

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
ru.WelcomeLabel2=CehoProxy отправляет через ваш VPN только выбранные программы, например Claude, Codex или Cursor. Банк, Госуслуги и браузер работают как обычно, напрямую. Ничего не нужно включать и выключать.%n%nДальше вы вставите ссылку на подписку и выберете программы. Это можно сделать и позже в панели.
en.WelcomeLabel2=CehoProxy sends only the programs you choose through your VPN, for example Claude, Codex or Cursor. Your bank, government sites and browser keep working directly. Nothing to switch on and off.%n%nNext you paste a subscription link and pick the programs. You can also do it later in the panel.

[CustomMessages]
ru.TaskDesktop=Ярлык панели CehoProxy на рабочем столе
ru.TaskTray=Значок CehoProxy в области уведомлений при входе в систему
ru.TaskGroup=Дополнительно:
ru.PageTitle=Первая настройка
ru.PageHint=Всё это можно пропустить и сделать позже в панели CehoProxy.
ru.SubLabel=Ссылка на подписку от вашего VPN-сервиса:
ru.AppsTitle=Программы для туннеля
ru.AppsHint=Отметьте программы, которым нужен VPN. Остальные будут работать напрямую. Рекомендуемые отмечены заранее.
ru.AppsLoading=Ищем программы на этом компьютере...
ru.AppsNone=Знакомых программ не нашлось. Добавить их можно позже в панели CehoProxy.
ru.AppsAll=Выбрать все
ru.AppsClear=Снять все
ru.GroupRec=Рекомендуемые
ru.GroupBrowser=Браузеры
ru.GroupMsg=Мессенджеры
ru.GroupOther=Остальные программы
ru.Protect=Включить защиту сразу и запускать её при старте системы
ru.StatusSetup=Проверяем подписку и настраиваем программы...
ru.RunPanel=Открыть панель CehoProxy
ru.RunSetup=Настроить сейчас
ru.ReadySub=Ссылка на подписку: %1
ru.ReadyNoSub=Ссылка на подписку: не указана, добавите в панели
ru.ReadyApps=Программы в туннеле:
ru.ReadyNoApps=Программы в туннеле: не выбраны, добавите в панели
ru.ReadyProtectOn=Защита: включить сразу и запускать при старте системы
ru.ReadyProtectOff=Защита: не включать сейчас
en.TaskDesktop=CehoProxy panel shortcut on the desktop
en.TaskTray=CehoProxy tray icon at sign-in
en.TaskGroup=Additional options:
en.PageTitle=First setup
en.PageHint=You can skip all of this and do it later in the CehoProxy panel.
en.SubLabel=Subscription link from your VPN service:
en.AppsTitle=Programs for the tunnel
en.AppsHint=Tick the programs that need the VPN. The rest keep working directly. Recommended ones are ticked.
en.AppsLoading=Looking for programs on this computer...
en.AppsNone=No familiar programs found. You can add them later in the CehoProxy panel.
en.AppsAll=Select all
en.AppsClear=Clear all
en.GroupRec=Recommended
en.GroupBrowser=Browsers
en.GroupMsg=Messengers
en.GroupOther=Other programs
en.Protect=Turn protection on now and start it with the system
en.StatusSetup=Checking the subscription and setting up programs...
en.RunPanel=Open the CehoProxy panel
en.RunSetup=Set up now
en.ReadySub=Subscription link: %1
en.ReadyNoSub=Subscription link: not set, you can add it in the panel
en.ReadyApps=Programs in the tunnel:
en.ReadyNoApps=Programs in the tunnel: none chosen, you can add them in the panel
en.ReadyProtectOn=Protection: turn on now and start with the system
en.ReadyProtectOff=Protection: do not turn on now

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; GroupDescription: "{cm:TaskGroup}"
Name: "trayautostart"; Description: "{cm:TaskTray}"; GroupDescription: "{cm:TaskGroup}"

[Files]
Source: "..\publish\cehoproxy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\cehoproxy-tray.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\README.md";            DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";              DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY.md";       DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\libcronet.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Панель CehoProxy"; Filename: "{app}\cehoproxy.exe"; Parameters: "open"
Name: "{group}\Значок CehoProxy"; Filename: "{app}\cehoproxy-tray.exe"
Name: "{commonstartup}\CehoProxy"; Filename: "{app}\cehoproxy-tray.exe"; Tasks: trayautostart
Name: "{commondesktop}\CehoProxy"; Filename: "{app}\cehoproxy.exe"; Parameters: "open"; Tasks: desktopicon
Name: "{group}\Страница CehoProxy"; Filename: "{#RepoUrl}"
Name: "{group}\Удалить CehoProxy"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\cehoproxy.exe"; Parameters: "install --no-setup --with-engine"; \
  StatusMsg: "Регистрируем программу и скачиваем движок sing-box..."; \
  Flags: runhidden waituntilterminated
Filename: "{app}\cehoproxy.exe"; Parameters: "{code:SetupParams}"; \
  StatusMsg: "{cm:StatusSetup}"; Flags: runhidden waituntilterminated; Check: HasSetupChoices
Filename: "{app}\cehoproxy.exe"; Parameters: "open"; \
  Description: "{cm:RunPanel}"; Flags: postinstall skipifsilent nowait; Check: HasSetupChoices
Filename: "{cmd}"; Parameters: "/k ""{app}\cehoproxy.exe"" setup"; \
  Description: "{cm:RunSetup}"; Flags: postinstall skipifsilent; Check: not HasSetupChoices

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM cehoproxy-tray.exe"; \
  Flags: runhidden waituntilterminated; RunOnceId: "cehoproxy_tray_stop"
Filename: "{app}\cehoproxy.exe"; Parameters: "uninstall --yes"; \
  Flags: runhidden waituntilterminated; RunOnceId: "cehoproxy_cleanup"

[UninstallDelete]
Type: files; Name: "{app}\cehoproxy-tray.exe"
Type: files; Name: "{commonstartup}\CehoProxy.lnk"
Type: files; Name: "{app}\chp.cmd"
Type: files; Name: "{app}\ceho-engine.exe"
Type: files; Name: "{app}\sing-box.exe"
Type: files; Name: "{app}\libcronet.dll"
Type: files; Name: "{app}\singbox.json"
Type: files; Name: "{app}\config.json"
Type: files; Name: "{app}\panel.port"
Type: files; Name: "{app}\cehoproxy.pid"
Type: files; Name: "{app}\sub-*.txt"
Type: files; Name: "{app}\cehoproxy.log"
Type: files; Name: "{app}\cehoproxy.log.*"
Type: files; Name: "{app}\cehoproxy-update.log"
Type: files; Name: "{app}\cehoproxy-update.log.*"
Type: files; Name: "{app}\update-status.json"
Type: files; Name: "{app}\update-status.json.tmp"
Type: files; Name: "{app}\sing-box.log"
Type: files; Name: "{app}\sing-box.log.*"
Type: files; Name: "{app}\crash-*.log"
Type: files; Name: "{app}\hwid.txt"
Type: files; Name: "{app}\node-country-cache.json"
Type: files; Name: "{app}\node-country-cache.json.tmp"
Type: files; Name: "{app}\tun-devices.txt"
Type: files; Name: "{app}\dbip-country-lite.mmdb"
Type: files; Name: "{app}\dbip-country-lite.mmdb.tmp"
Type: files; Name: "{app}\dbip-country-lite.mmdb.gz.tmp"
Type: files; Name: "{app}\dbip-country-lite.mmdb.bundled.tmp"
Type: files; Name: "{app}\sing-box-*.zip"
Type: files; Name: "{app}\sing-box-*.tar.gz"
Type: files; Name: "{app}\cehoproxy.exe.old"
Type: files; Name: "{app}\cehoproxy.exe.new"
Type: files; Name: "{app}\cehoproxy.before-*"
Type: files; Name: "{app}\ceho-engine.exe.old"
Type: files; Name: "{app}\ceho-engine.exe.new"
Type: files; Name: "{app}\sing-box.exe.old"
Type: files; Name: "{app}\sing-box.exe.new"
Type: files; Name: "{app}\libcronet.dll.old"
Type: files; Name: "{app}\libcronet.dll.new"
Type: files; Name: "{app}\libcronet.dll.dl"
Type: files; Name: "{app}\config.before-*.json"
Type: files; Name: "{app}\relaunch.ps1"
Type: files; Name: "{app}\relaunch.sh"
Type: files; Name: "{app}\update-relaunch.ps1"
Type: files; Name: "{app}\update-relaunch.sh"
Type: files; Name: "{app}\pick-app.ps1"
Type: files; Name: "{app}\pick-app-launch.vbs"
Type: files; Name: "{app}\pick-app-result.txt"
Type: files; Name: "{app}\user-alias.path"
Type: files; Name: "{app}\.write-probe"
Type: files; Name: "{app}\.write-test"
Type: filesandordirs; Name: "{app}\geo-probes"
Type: filesandordirs; Name: "{app}\geoip"
Type: filesandordirs; Name: "{app}\engine-tmp"
Type: dirifempty; Name: "{app}"

[Code]
var
  SetupPage: TWizardPage;
  SubEdit: TNewEdit;
  ProtectBox: TNewCheckBox;
  AppsPage: TWizardPage;
  AppList: TNewCheckListBox;
  AppPaths: TArrayOfString;
  AppsLoaded: Boolean;
  AppsInfo: TNewStaticText;

var
  AllBtn, ClearBtn: TNewButton;

procedure SetAll(Value: Boolean);
var
  I: Integer;
begin
  for I := 0 to AppList.Items.Count - 1 do
    if AppList.ItemLevel[I] = 1 then
      AppList.Checked[I] := Value;
end;

procedure SelectAll(Sender: TObject);
begin
  SetAll(True);
end;

procedure ClearAll(Sender: TObject);
begin
  SetAll(False);
end;

procedure AddGroupHeader(Kind: String);
var
  Caption: String;
begin
  if Kind = 'R' then Caption := ExpandConstant('{cm:GroupRec}')
  else if Kind = 'B' then Caption := ExpandConstant('{cm:GroupBrowser}')
  else if Kind = 'M' then Caption := ExpandConstant('{cm:GroupMsg}')
  else Caption := ExpandConstant('{cm:GroupOther}');
  AppList.AddGroup(Caption, '', 0, nil);
end;

procedure LoadApps;
var
  Lines: TArrayOfString;
  Exe, ListFile, Kind, Line, Name, Path: String;
  ResultCode, I, K, P, Idx: Integer;
  Header, Any: Boolean;
begin
  if AppsLoaded then Exit;
  AppsLoaded := True;
  ExtractTemporaryFile('cehoproxy.exe');
  Exe := ExpandConstant('{tmp}\cehoproxy.exe');
  ListFile := ExpandConstant('{tmp}\apps.txt');
  DeleteFile(ListFile);
  Exec(Exe, 'detect-apps --out "' + ListFile + '"', ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if not LoadStringsFromFile(ListFile, Lines) then
    SetArrayLength(Lines, 0);

  SetArrayLength(AppPaths, GetArrayLength(Lines) + 16);
  Any := False;
  for K := 1 to 4 do
  begin
    Kind := Copy('RBMO', K, 1);
    Header := False;
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
      Line := Lines[I];
      if (Length(Line) < 4) or (Copy(Line, 1, 1) <> Kind) then Continue;
      Delete(Line, 1, 2);
      P := Pos(#9, Line);
      if P = 0 then Continue;
      Name := Copy(Line, 1, P - 1);
      Path := Trim(Copy(Line, P + 1, Length(Line)));
      if not Header then
      begin
        AddGroupHeader(Kind);
        Header := True;
      end;
      Idx := AppList.AddCheckBox(Name, '', 1, Kind = 'R', True, False, False, nil);
      AppPaths[Idx] := Path;
      Any := True;
    end;
  end;
  if Any then AppsInfo.Caption := '' else AppsInfo.Caption := ExpandConstant('{cm:AppsNone}');
end;

procedure AppsPageActivate(Sender: TWizardPage);
begin
  LoadApps;
end;

procedure InitializeWizard;
var
  Label1, Label2: TNewStaticText;
begin
  SetupPage := CreateCustomPage(wpSelectTasks, ExpandConstant('{cm:PageTitle}'), ExpandConstant('{cm:PageHint}'));

  Label1 := TNewStaticText.Create(SetupPage);
  Label1.Parent := SetupPage.Surface;
  Label1.Caption := ExpandConstant('{cm:SubLabel}');
  Label1.Left := 0;
  Label1.Top := 0;

  SubEdit := TNewEdit.Create(SetupPage);
  SubEdit.Parent := SetupPage.Surface;
  SubEdit.Left := 0;
  SubEdit.Top := Label1.Top + Label1.Height + ScaleY(6);
  SubEdit.Width := SetupPage.SurfaceWidth;
  SubEdit.Text := ExpandConstant('{param:SUB|}');

  ProtectBox := TNewCheckBox.Create(SetupPage);
  ProtectBox.Parent := SetupPage.Surface;
  ProtectBox.Left := 0;
  ProtectBox.Top := SubEdit.Top + SubEdit.Height + ScaleY(20);
  ProtectBox.Width := SetupPage.SurfaceWidth;
  ProtectBox.Caption := ExpandConstant('{cm:Protect}');
  ProtectBox.Checked := True;

  AppsPage := CreateCustomPage(SetupPage.ID, ExpandConstant('{cm:AppsTitle}'), ExpandConstant('{cm:AppsHint}'));
  AppsPage.OnActivate := @AppsPageActivate;

  AppsInfo := TNewStaticText.Create(AppsPage);
  AppsInfo.Parent := AppsPage.Surface;
  AppsInfo.Left := 0;
  AppsInfo.Top := 0;
  AppsInfo.Width := AppsPage.SurfaceWidth;
  AppsInfo.Caption := ExpandConstant('{cm:AppsLoading}');

  AppList := TNewCheckListBox.Create(AppsPage);
  AppList.Parent := AppsPage.Surface;
  AppList.Left := 0;
  AppList.Top := ScaleY(22);
  AppList.Width := AppsPage.SurfaceWidth;
  AppList.Height := AppsPage.SurfaceHeight - ScaleY(22) - ScaleY(34);

  AllBtn := TNewButton.Create(AppsPage);
  AllBtn.Parent := AppsPage.Surface;
  AllBtn.Caption := ExpandConstant('{cm:AppsAll}');
  AllBtn.Width := ScaleX(120);
  AllBtn.Left := 0;
  AllBtn.Top := AppList.Top + AppList.Height + ScaleY(6);
  AllBtn.OnClick := @SelectAll;

  ClearBtn := TNewButton.Create(AppsPage);
  ClearBtn.Parent := AppsPage.Surface;
  ClearBtn.Caption := ExpandConstant('{cm:AppsClear}');
  ClearBtn.Width := ScaleX(120);
  ClearBtn.Left := AllBtn.Width + ScaleX(8);
  ClearBtn.Top := AllBtn.Top;
  ClearBtn.OnClick := @ClearAll;
end;

function CleanLink: String;
begin
  Result := Trim(SubEdit.Text);
  StringChangeEx(Result, '"', '', True);
end;

function AnyAppChecked: Boolean;
var
  I: Integer;
begin
  Result := False;
  if not AppsLoaded then Exit;
  for I := 0 to AppList.Items.Count - 1 do
    if (AppList.ItemLevel[I] = 1) and AppList.Checked[I] then
    begin
      Result := True;
      Exit;
    end;
end;

function HasSetupChoices: Boolean;
begin
  if WizardSilent then
    Result := CleanLink <> ''
  else
    Result := (CleanLink <> '') or AnyAppChecked;
end;

function SetupParams(Param: String): String;
var
  I: Integer;
begin
  Result := 'setup --lang ' + ExpandConstant('{language}');
  if CleanLink <> '' then Result := Result + ' --sub "' + CleanLink + '"';
  if WizardSilent then
  begin
    if ExpandConstant('{param:APPS|all}') <> 'none' then Result := Result + ' --all-apps';
  end
  else if AppsLoaded then
    for I := 0 to AppList.Items.Count - 1 do
      if (AppList.ItemLevel[I] = 1) and AppList.Checked[I] then
        Result := Result + ' --app "' + AppPaths[I] + '"';
  if ProtectBox.Checked then Result := Result + ' --autostart';
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  I: Integer;
  Apps: String;
begin
  Result := '';
  if MemoTasksInfo <> '' then Result := MemoTasksInfo + NewLine + NewLine;

  if CleanLink <> '' then
    Result := Result + FmtMessage(ExpandConstant('{cm:ReadySub}'), [CleanLink]) + NewLine
  else
    Result := Result + ExpandConstant('{cm:ReadyNoSub}') + NewLine;

  Apps := '';
  if AppsLoaded then
    for I := 0 to AppList.Items.Count - 1 do
      if (AppList.ItemLevel[I] = 1) and AppList.Checked[I] then
        Apps := Apps + Space + Space + AppList.ItemCaption[I] + NewLine;
  if Apps <> '' then
    Result := Result + ExpandConstant('{cm:ReadyApps}') + NewLine + Apps
  else
    Result := Result + ExpandConstant('{cm:ReadyNoApps}') + NewLine;

  if ProtectBox.Checked then
    Result := Result + ExpandConstant('{cm:ReadyProtectOn}')
  else
    Result := Result + ExpandConstant('{cm:ReadyProtectOff}');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if not FileExists(ExpandConstant('{app}\cehoproxy.exe')) then
    Exit;

  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM cehoproxy-tray.exe',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);

  ExtractTemporaryFile('cehoproxy.exe');
  if not Exec(ExpandConstant('{tmp}\cehoproxy.exe'), '_prepare-install',
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
    Result := 'Не удалось остановить работающий CehoProxy. Установка отменена без удаления настроек.';
end;

// Сам деинсталлятор удалить себя не может: он в этот момент работает. Windows умеет
// удалить файл при следующей перезагрузке — просим её об этом, иначе после «полного
// удаления» в папке навсегда остаётся четырёхмегабайтный файл. Поймано живьём.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RestartReplace(ExpandConstant('{uninstallexe}'), '');
    RestartReplace(ExpandConstant('{app}'), '');
  end;
end;
