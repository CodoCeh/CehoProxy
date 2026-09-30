#define AppName "CehoProxy"
#define AppVersion "1.2.111"
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
SolidCompression=yes
WizardStyle=modern
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
ru.AllApps=Отправить в туннель все найденные на этом компьютере рекомендуемые программы
ru.Protect=Включить защиту сразу и запускать её при старте системы
ru.StatusSetup=Проверяем подписку и настраиваем программы...
ru.RunPanel=Открыть панель CehoProxy
ru.RunSetup=Настроить сейчас
en.TaskDesktop=CehoProxy panel shortcut on the desktop
en.TaskTray=CehoProxy tray icon at sign-in
en.TaskGroup=Additional options:
en.PageTitle=First setup
en.PageHint=You can skip all of this and do it later in the CehoProxy panel.
en.SubLabel=Subscription link from your VPN service:
en.AllApps=Send all recommended programs found on this computer through the tunnel
en.Protect=Turn protection on now and start it with the system
en.StatusSetup=Checking the subscription and setting up programs...
en.RunPanel=Open the CehoProxy panel
en.RunSetup=Set up now

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
  AppsBox, ProtectBox: TNewCheckBox;

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

  AppsBox := TNewCheckBox.Create(SetupPage);
  AppsBox.Parent := SetupPage.Surface;
  AppsBox.Left := 0;
  AppsBox.Top := SubEdit.Top + SubEdit.Height + ScaleY(20);
  AppsBox.Width := SetupPage.SurfaceWidth;
  AppsBox.Caption := ExpandConstant('{cm:AllApps}');
  AppsBox.Checked := True;

  ProtectBox := TNewCheckBox.Create(SetupPage);
  ProtectBox.Parent := SetupPage.Surface;
  ProtectBox.Left := 0;
  ProtectBox.Top := AppsBox.Top + AppsBox.Height + ScaleY(10);
  ProtectBox.Width := SetupPage.SurfaceWidth;
  ProtectBox.Caption := ExpandConstant('{cm:Protect}');
  ProtectBox.Checked := True;
end;

function CleanLink: String;
begin
  Result := Trim(SubEdit.Text);
  StringChangeEx(Result, '"', '', True);
end;

function HasSetupChoices: Boolean;
begin
  Result := (CleanLink <> '') or AppsBox.Checked;
  if WizardSilent and (CleanLink = '') then Result := False;
end;

function SetupParams(Param: String): String;
begin
  Result := 'setup --lang ' + ExpandConstant('{language}');
  if CleanLink <> '' then Result := Result + ' --sub "' + CleanLink + '"';
  if AppsBox.Checked then Result := Result + ' --all-apps';
  if ProtectBox.Checked then Result := Result + ' --autostart';
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
