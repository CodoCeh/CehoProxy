#define AppName "CehoProxy"
#define AppVersion "1.2.11"
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
UninstallDisplayIcon={app}\cehoproxy.exe
UninstallDisplayName={#AppName}

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\cehoproxy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";            DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";              DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY.md";       DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Панель CehoProxy"; Filename: "{app}\cehoproxy.exe"; Parameters: "open"
Name: "{group}\Страница CehoProxy"; Filename: "{#RepoUrl}"
Name: "{group}\Удалить CehoProxy"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\cehoproxy.exe"; Parameters: "install --no-setup --with-engine"; \
  StatusMsg: "Регистрируем программу и скачиваем движок sing-box..."; \
  Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/k ""{app}\cehoproxy.exe"" setup"; \
  Description: "Настроить сейчас"; Flags: postinstall skipifsilent

[UninstallRun]
Filename: "{app}\cehoproxy.exe"; Parameters: "uninstall --yes"; \
  Flags: runhidden waituntilterminated; RunOnceId: "cehoproxy_cleanup"

[UninstallDelete]
Type: files; Name: "{app}\chp.cmd"
Type: files; Name: "{app}\ceho-engine.exe"
Type: files; Name: "{app}\sing-box.exe"
Type: files; Name: "{app}\singbox.json"
Type: files; Name: "{app}\config.json"
Type: files; Name: "{app}\panel.port"
Type: files; Name: "{app}\cehoproxy.pid"
Type: files; Name: "{app}\sub-*.txt"
Type: files; Name: "{app}\*.log"
Type: dirifempty; Name: "{app}"

[Code]
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
