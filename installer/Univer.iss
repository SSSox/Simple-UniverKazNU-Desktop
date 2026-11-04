; Built only from a verified portable release by scripts/build-installer.ps1.
#if Flavor == "modern"
  #define FlavorLabel "Modern"
  #define MinimumWindows "10.0"
  #define NetKind "net462"
  #define NetLabel ".NET Framework 4.6.2"
#else
  #define FlavorLabel "Legacy"
  #define MinimumWindows "6.1sp1"
  #define NetKind "net452"
  #define NetLabel ".NET Framework 4.5.2"
#endif
#ifndef InstallId
  #define InstallId "Univer.Desktop." + Flavor + "." + Arch
#endif
#define ProductName "Univer КазНУ (" + FlavorLabel + " " + Arch + ")"

[Setup]
AppId={#InstallId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName={#ProductName} {#AppVersion}
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=Установка {#ProductName}
VersionInfoProductName=Simple-UniverKazNU
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Univer\{#Flavor}-{#Arch}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
MinVersion={#MinimumWindows}
#if Arch == "x64"
ArchitecturesAllowed=x64os
#else
ArchitecturesAllowed=x86compatible and not arm64
#endif
SetupArchitecture=x86
WizardStyle=modern dynamic
WizardSizePercent=110
DisableWelcomePage=no
DisableDirPage=yes
DisableProgramGroupPage=no
AllowNoIcons=yes
SetupIconFile=..\src\Univer.Desktop\app.ico
UninstallDisplayIcon={app}\Univer.Desktop.exe
UninstallDisplayName={#ProductName}
AppMutex=Local\Univer.Desktop.{username}
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
OutputDir={#OutputRoot}
OutputBaseFilename=Univer-Desktop-{#AppVersion}-{#Flavor}-{#Arch}-setup
Compression=lzma2
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMADictionarySize=32768
InfoBeforeFile={#InfoFile}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "kazakh"; MessagesFile: "Kazakh.isl"

[LangOptions]
DialogFontName=Segoe UI
DialogFontSize=9

[CustomMessages]
#if Flavor == "legacy"
russian.EditionNotice=Univer Legacy: версия для Windows 7 SP1, 8 и 8.1.%n%nВ этой версии используется устаревший браузер с известными уязвимостями. Для Windows 10 и 11 используйте Univer Modern.%n%nПредупреждение также доступно в настройках приложения.%n%nОбновление и удаление приложения сохраняют данные входа и настройки текущего пользователя Windows.%n%nНеофициальное приложение: не связано с КазНУ им. аль-Фараби. Вход и данные остаются на сайте univer.kaznu.kz.
english.EditionNotice=Univer Legacy is for Windows 7 SP1, 8 and 8.1.%n%nThis edition uses an outdated browser with known vulnerabilities. Use Univer Modern on Windows 10 and 11.%n%nThe warning is also available in application settings.%n%nUpdating or uninstalling the application preserves the current Windows user's login data and settings.%n%nUnofficial application, not affiliated with Al-Farabi Kazakh National University. Sign-in and data stay on univer.kaznu.kz.
kazakh.EditionNotice=Univer Legacy: Windows 7 SP1, 8 және 8.1 жүйелеріне арналған нұсқа.%n%nБұл нұсқада белгілі осалдықтары бар ескірген браузер қолданылады. Windows 10 және 11 үшін Univer Modern пайдаланыңыз.%n%nЕскерту қолданба баптауларында да көрсетіледі.%n%nҚолданбаны жаңарту немесе жою қазіргі Windows пайдаланушысының кіру деректері мен баптауларын сақтайды.%n%nРесми емес қолданба, әл-Фараби атындағы ҚазҰУ-мен байланысы жоқ. Кіру мен деректер univer.kaznu.kz сайтында қалады.
#else
russian.EditionNotice=Univer Modern: версия для Windows 10 и 11.%n%nУстановка выполняется для текущего пользователя Windows.%n%nОбновление и удаление приложения сохраняют данные входа и настройки.%n%nНеофициальное приложение: не связано с КазНУ им. аль-Фараби. Вход и данные остаются на сайте univer.kaznu.kz.
english.EditionNotice=Univer Modern is for Windows 10 and 11.%n%nSetup installs the application for the current Windows user.%n%nUpdating or uninstalling the application preserves login data and settings.%n%nUnofficial application, not affiliated with Al-Farabi Kazakh National University. Sign-in and data stay on univer.kaznu.kz.
kazakh.EditionNotice=Univer Modern: Windows 10 және 11 жүйелеріне арналған нұсқа.%n%nҚолданба қазіргі Windows пайдаланушысына орнатылады.%n%nҚолданбаны жаңарту немесе жою кіру деректері мен баптауларды сақтайды.%n%nРесми емес қолданба, әл-Фараби атындағы ҚазҰУ-мен байланысы жоқ. Кіру мен деректер univer.kaznu.kz сайтында қалады.
#endif
russian.DesktopShortcut=Создать ярлык на рабочем столе
russian.Launch=Открыть Univer
russian.Prerequisites=Компоненты Windows
russian.PrerequisitesDescription=Для работы приложения нужны дополнительные компоненты
russian.PrerequisitesBody=Не установлены:%n%n%1%n%nУстановщик загрузит их с сайта Microsoft и проверит файлы. Для установки компонентов могут понадобиться права администратора.%n%nЕсли потребуется перезагрузка, сохраните работу и повторите установку после неё.
russian.PrerequisitesConsent=Загрузить и установить недостающие компоненты Microsoft? Windows может запросить права администратора.
russian.PrerequisitesSilent=Сначала установите необходимые компоненты Microsoft и повторите установку приложения
russian.DownloadFailed=Не удалось загрузить компоненты. Проверьте подключение к интернету и повторите установку
russian.InstallFailed=Не удалось установить компонент: %1. Код: %2. Установите его вручную и повторите установку приложения
russian.RestartRequired=Для завершения установки компонентов перезагрузите Windows. Затем снова запустите установщик Univer
russian.InvalidFolder=Эта папка не подходит для установки. Используйте отдельную пустую папку или папку этой версии Univer
russian.NewerVersion=Уже установлена более новая версия Univer. Используйте её установщик
english.DesktopShortcut=Create a desktop shortcut
english.Launch=Open Univer
english.Prerequisites=Windows components
english.PrerequisitesDescription=Additional components are required
english.PrerequisitesBody=Not installed:%n%n%1%n%nSetup will download these components from Microsoft and verify the files. Administrator permission may be required.%n%nIf a restart is required, save your work and run setup again after restarting.
english.PrerequisitesConsent=Download and install the missing Microsoft components? Windows may request administrator permission.
english.PrerequisitesSilent=Install the required Microsoft components first, then run application setup again
english.DownloadFailed=Unable to download the components. Check your internet connection and run setup again
english.InstallFailed=Unable to install component: %1. Code: %2. Install it manually, then run application setup again
english.RestartRequired=Restart Windows to complete component installation, then run Univer setup again
english.InvalidFolder=Choose a separate empty folder or the installation folder for this edition of Univer
english.NewerVersion=A newer version of Univer is already installed. Use its installer
kazakh.DesktopShortcut=Жұмыс үстелінде жарлық жасау
kazakh.Launch=Univer ашу
kazakh.Prerequisites=Windows компоненттері
kazakh.PrerequisitesDescription=Қолданбаға қосымша компоненттер қажет
kazakh.PrerequisitesBody=Орнатылмаған:%n%n%1%n%nОрнатушы оларды Microsoft сайтынан жүктеп, файлдарды тексереді. Компоненттерді орнату үшін әкімші құқығы қажет болуы мүмкін.%n%nҚайта іске қосу қажет болса, жұмысыңызды сақтап, Windows қайта іске қосылған соң орнатуды қайталаңыз.
kazakh.PrerequisitesConsent=Жетіспейтін Microsoft компоненттерін жүктеп, орнату керек пе? Windows әкімші құқығын сұрауы мүмкін.
kazakh.PrerequisitesSilent=Алдымен қажетті Microsoft компоненттерін орнатыңыз. Содан кейін қолданбаны орнатуды қайталаңыз
kazakh.DownloadFailed=Компоненттер жүктелмеді. Интернет қосылымын тексеріп, орнатуды қайталаңыз
kazakh.InstallFailed=Компонент орнатылмады: %1. Код: %2. Оны қолмен орнатып, қолданбаны орнатуды қайталаңыз
kazakh.RestartRequired=Компоненттерді орнатуды аяқтау үшін Windows жүйесін қайта іске қосыңыз. Содан кейін Univer орнатушыны қайта іске қосыңыз
kazakh.InvalidFolder=Бөлек бос буманы немесе Univer бағдарламасының осы нұсқасы орнатылған буманы таңдаңыз
kazakh.NewerVersion=Univer бағдарламасының жаңарақ нұсқасы орнатылған. Сол нұсқаның орнатушысын пайдаланыңыз

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: unchecked

[InstallDelete]
; Up to 0.12.1 the package also carried the project's working notes; an upgrade removes them.
Type: filesandordirs; Name: "{app}\docs"
Type: files; Name: "{app}\COMPATIBILITY.md"
Type: files; Name: "{app}\VALIDATION.md"
Type: files; Name: "{app}\ROADMAP.md"

[Files]
Source: "{#PayloadRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MarkerFile}"; DestDir: "{app}"; DestName: "installation-id.txt"; Flags: ignoreversion
Source: "compiler:license.txt"; DestDir: "{app}\licenses"; DestName: "Inno-Setup.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Univer"; Filename: "{app}\Univer.Desktop.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\Univer.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Univer.Desktop.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: CanLaunch
Filename: "{app}\Univer.Desktop.exe"; Flags: nowait skipifnotsilent runasoriginaluser; Check: IsAppUpdate

[Code]
var
  DownloadPage: TDownloadWizardPage;
  ComponentPage: TOutputMsgWizardPage;
  RestartPending: Boolean;

function GetFileAttributesW(Name: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function AppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

// The running app starts this update and closes itself; AppMutex would otherwise find it still open.
function InitializeSetup: Boolean;
var
  Waited: Integer;
begin
  Result := True;
  if not AppUpdate then exit;
  Waited := 0;
  while CheckForMutexes(ExpandConstant('Local\Univer.Desktop.{username}')) and (Waited < 120) do begin
    Sleep(250);
    Waited := Waited + 1;
  end;
end;

function MissingNet: Boolean;
begin
  Result := not IsDotNetInstalled({#NetKind}, 0);
end;

function MissingVC: Boolean;
var
  Installed: Cardinal;
  VersionText: String;
  InstalledVersion, RequiredVersion: Int64;
begin
  Result := True;
  if not RegQueryDWordValue(HKLM32,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{#Arch}', 'Installed', Installed) then exit;
  if Installed <> 1 then exit;
  if not RegQueryStringValue(HKLM32,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{#Arch}', 'Version', VersionText) then exit;
  if Copy(VersionText, 1, 1) = 'v' then Delete(VersionText, 1, 1);
  if not StrToVersion(VersionText, InstalledVersion) then exit;
  if not StrToVersion('{#VCVersion}', RequiredVersion) then exit;
  Result := ComparePackedVersion(InstalledVersion, RequiredVersion) < 0;
end;

function MissingComponents: String;
begin
  Result := '';
  if MissingNet then Result := '{#NetLabel}';
  if MissingVC then begin
    if Result <> '' then Result := Result + #13#10;
    Result := Result + 'Microsoft Visual C++ {#VCVersion} ({#Arch})';
  end;
end;

function IsWithin(Path, Root: String): Boolean;
begin
  Result := (CompareText(Path, Root) = 0) or
    (CompareText(Copy(Path, 1, Length(Root) + 1), Root + '\') = 0);
end;

function SafeInstallDirectory: Boolean;
var
  Target, DataRoot, Parent: String;
  Marker: AnsiString;
  Finder: TFindRec;
  Attributes: LongWord;
begin
  Result := False;
  Target := RemoveBackslash(ExpandFileName(WizardDirValue));
  // Only local drive paths; UNC share roots can have a non-progressing parent traversal.
  if Copy(Target, 2, 2) <> ':\' then exit;
  DataRoot := RemoveBackslash(ExpandConstant('{localappdata}\Univer.Desktop'));
  if IsWithin(Target, DataRoot) or IsWithin(DataRoot, Target) then exit;
  Parent := Target;
  repeat
    Attributes := GetFileAttributesW(Parent);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then exit;
    Parent := RemoveBackslash(ExtractFileDir(Parent));
  until (Length(Parent) <= 3);
  if Length(Target) <= 3 then exit;
  if FileExists(Target) then exit;
  if not DirExists(Target) then begin Result := True; exit; end;
  if LoadStringFromFile(Target + '\installation-id.txt', Marker) then begin
    Result := Trim(Marker) = '{#InstallId}';
    exit;
  end;
  if FindFirst(Target + '\*', Finder) then begin
    try
      repeat
        if (Finder.Name <> '.') and (Finder.Name <> '..') then exit;
      until not FindNext(Finder);
    finally
      FindClose(Finder);
    end;
  end;
  Result := True;
end;

procedure InitializeWizard;
var
  Missing: String;
begin
  WizardForm.InfoBeforeMemo.Text := FmtMessage(CustomMessage('EditionNotice'), []);
  Missing := MissingComponents;
  Log('Required components: ' + Missing);
  if Missing <> '' then
    ComponentPage := CreateOutputMsgPage(wpInfoBefore, CustomMessage('Prerequisites'),
      CustomMessage('PrerequisitesDescription'), FmtMessage(CustomMessage('PrerequisitesBody'), [Missing]));
  DownloadPage := CreateDownloadPage(CustomMessage('Prerequisites'), SetupMessage(msgPreparingDesc), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function InstallComponent(Name, FileName, Parameters: String): String;
var
  Code: Integer;
begin
  Result := '';
  if not ShellExec('runas', ExpandConstant('{tmp}\') + FileName, Parameters, '',
    SW_HIDE, ewWaitUntilTerminated, Code) then begin
    Result := FmtMessage(CustomMessage('InstallFailed'), [Name, IntToStr(Code)]);
    exit;
  end;
  if (Code = 3010) or (Code = 1641) then RestartPending := True
  else if Code <> 0 then
    Result := FmtMessage(CustomMessage('InstallFailed'), [Name, IntToStr(Code)]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion, PackageVersion: Int64;
  InstalledVersionText: String;
  NeedNet, NeedVC: Boolean;
begin
  Result := '';
  if RestartPending then begin Result := CustomMessage('RestartRequired'); exit; end;
  if not SafeInstallDirectory then begin Result := CustomMessage('InvalidFolder'); exit; end;
  if GetVersionNumbersString(ExpandConstant('{app}\Univer.Desktop.exe'), InstalledVersionText) and
    StrToVersion(InstalledVersionText, InstalledVersion) and
    StrToVersion('{#AppVersion}.0', PackageVersion) then
    if ComparePackedVersion(InstalledVersion, PackageVersion) > 0 then begin
      Result := CustomMessage('NewerVersion'); exit;
    end;
  NeedNet := MissingNet;
  NeedVC := MissingVC;
  if not NeedNet and not NeedVC then exit;
  // Silent installation never installs system components or triggers UAC without consent.
  if WizardSilent then begin Result := CustomMessage('PrerequisitesSilent'); exit; end;
  if SuppressibleMsgBox(CustomMessage('PrerequisitesConsent'), mbConfirmation,
    MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then begin
    Result := CustomMessage('PrerequisitesSilent'); exit;
  end;
  DownloadPage.Clear;
  if NeedNet then DownloadPage.Add('{#NetUrl}', 'dotnet-framework.exe', '{#NetSHA256}');
  if NeedVC then DownloadPage.Add('{#VCUrl}', 'visual-cpp.exe', '{#VCSHA256}');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      Log('Component download failed: ' + GetExceptionMessage);
      Result := CustomMessage('DownloadFailed');
    end;
  finally
    DownloadPage.Hide;
  end;
  if Result <> '' then exit;
  if NeedNet then Result := InstallComponent('{#NetLabel}', 'dotnet-framework.exe', '/passive /norestart');
  if Result <> '' then exit;
  if RestartPending then begin Result := CustomMessage('RestartRequired'); exit; end;
  if NeedVC then Result := InstallComponent('Microsoft Visual C++', 'visual-cpp.exe', '/install /passive /norestart');
  if Result <> '' then exit;
  if RestartPending then begin Result := CustomMessage('RestartRequired'); exit; end;
  if MissingComponents <> '' then Result := CustomMessage('PrerequisitesSilent');
end;

function CanLaunch: Boolean;
begin
  Result := not RestartPending;
end;

function IsAppUpdate: Boolean;
begin
  Result := AppUpdate and CanLaunch;
end;

// No UninstallDelete section: only installer-owned files are removed.
// The separate localappdata\Univer.Desktop account directory is never owned by setup.
