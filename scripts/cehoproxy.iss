; Настоящий setup.exe для Windows. Собирается компилятором Inno Setup (ISCC.exe) на Windows:
;   iscc scripts\cehoproxy.iss
; Рядом должен лежать собранный cehoproxy.exe (win-x64, self-contained).
;
; Движок sing-box в установщик НЕ кладём: он под GPL, и раздавать его мы не будем.
; Программа предложит скачать его с сайта автора сама, уже после установки.

#define AppName "CehoProxy"
#define AppVersion "1.0.5"
#define AppPublisher "КодоЦех"
#define AppUrl "https://codoceh.ru"
#define RepoUrl "https://github.com/CodoCeh/CehoProxy"

[Setup]
AppId={{6E2C3F41-8B7A-4E2D-9C1F-2A5D7B0E9C33}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
; страница продукта — она же в «Программах и компонентах» Windows
; без этого свойства файла в Проводнике пустые: у установщика не видно ни версии, ни имени
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} — установка
VersionInfoCompany={#AppPublisher}
AppSupportURL={#RepoUrl}
AppUpdatesURL={#RepoUrl}
DefaultDirName={commonappdata}\CehoProxy
DisableDirPage=yes
DisableProgramGroupPage=yes
; без явного имени Inno кладёт ярлыки в папку «(Default)» — поймано живьём
DefaultGroupName={#AppName}
OutputBaseFilename=CehoProxy-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; TUN-интерфейс и запись в общий PATH требуют прав администратора
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
; фирменный знак — и на самом установщике, и в списке установленных программ
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
; сама установка: короткая команда chp, запись в PATH, предложение скачать движок,
; затем мастер настройки в отдельном окне терминала
Filename: "{app}\cehoproxy.exe"; Parameters: "install --no-setup"; \
  StatusMsg: "Регистрируем программу в системе..."; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/k ""{app}\cehoproxy.exe"" setup"; \
  Description: "Настроить сейчас"; Flags: postinstall skipifsilent

[UninstallRun]
; снять автозапуск, защиту и следы в системе до удаления файлов
Filename: "{app}\cehoproxy.exe"; Parameters: "uninstall --yes"; \
  Flags: runhidden waituntilterminated; RunOnceId: "cehoproxy_cleanup"
