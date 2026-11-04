[Русский](BUILD.md) · **Қазақша** · [English](BUILD.en.md)

# Құрастыру, тексеру және шығару

Қолданбаны осы бастапқы кодтан құрастыруға қажеттінің бәрі: портативті мұрағаттар, орнатушылар және тексерулер. Дайын қолданбаны пайдаланушыларға мұның ешқайсысы қажет емес.

## Не қажет

- Windows 10 немесе 11.
- [.NET SDK](https://dotnet.microsoft.com/download) (10.0.400 нұсқасында тексерілген). Қолданба .NET Framework 4.5.2 (Legacy) және 4.6.2 (Modern) үшін, тесттер 4.8 үшін құрастырылады; қажетті reference assemblies NuGet-тен келеді.
- PowerShell 7 (`pwsh`); 7.6 нұсқасында тексерілген.
- Node.js және npm тек бет тесттері үшін (`tests/dom`).
- Git тек `test-release-packaging.ps1` үшін.
- Алғашқы құрастыруда интернет: NuGet пакеттері және орнатушы компиляторы.

NuGet және npm пакеттері lock файлдарымен бекітілген (`packages.lock.json`, `packages.modern.lock.json`, `package-lock.json`); қалпына келтіру `--locked-mode` режимінде жүреді, нұсқаларды себепсіз өзгертпеңіз.

## Бір команда

Репозиторий түбірінен PowerShell 7-де:

```powershell
.\scripts\build-release.ps1 -Package Portable                 # портативті Modern x64
.\scripts\build-release.ps1 -Package Installer                # Modern x64 орнатушысы (және оның портативті негізі)
.\scripts\build-release.ps1 -Package Both -BrowserFlavor all -Platform all -Verify
```

Әр іске қосу `build\release-<күн>-<id>` ішінде жаңа құрастырма жасап, нәтижені жаңа `artifacts\release-<күн>-<id>` бумасына (немесе `-OutputDirectory` ішіне) салады. `-Verify` әр ZIP мұрағатын толық ашып, орнатушы жасалмас бұрын одан өзін-өзі тексеруді іске қосады. `-BrowserFlavor` мәндері: `Modern`, `Legacy` немесе `all`, `-Platform`: `x64`, `x86` немесе `all`. Алғашқы қатеде шығару тоқтайды.

## Қадам бойынша

```powershell
$build = '.\build\local'
$dist = '.\artifacts\local-' + (Get-Date -Format 'yyyyMMdd-HHmmss')

# 1. Барлық төрт нұсқаның қолданба файлдары, мұрағаттарсыз
.\scripts\build.ps1 -BuildOnly -OutputRoot $build
# 2. Портативті: ZIP, .sha256 және манифест
.\scripts\build.ps1 -SkipBuild -OutputRoot $build -ArtifactRoot $dist
# 3. Осы ZIP-тен орнатушылар
.\scripts\build-installer.ps1 -ArtifactRoot $dist
```

Бір нұсқа үшін барлық үш командаға бірдей `-BrowserFlavor` және `-Platform` беріңіз (мысалы, `-BrowserFlavor Modern -Platform x64`). Скрипттер бар ZIP пен орнатушыларды қайта жазбайды: сол нұсқаны қайта буып-түю үшін жаңа `$dist` бумасын көрсетіңіз.

Legacy CefSharp 109-бен құрастырылады, бұл кезде NuGet Chromium 109-дың белгілі осалдығы туралы `NU1903` ескертуін көрсетеді. Ол басылмайды: ол шындық.

## Нәтижесі

| Файл | Бұл не |
| --- | --- |
| `$build\<modern\|legacy>\<x64\|x86>\Univer.Desktop.exe` | Жанындағы DLL, Chromium ресурстары және `Assets` бар құрастырылған қолданба |
| `Univer-Desktop-<нұсқа>-<түрі>-<arch>.zip` | Портативті мұрағат: қолданба, `LICENSE`, үш тілдегі `README`, `SECURITY`, `PRIVACY`, `BUILD` және `THIRD-PARTY` (`.md`, `.kz.md`, `.en.md`), `licenses/`, `BUILD-INFO.txt` |
| `Univer-Desktop-<нұсқа>-<түрі>-<arch>-setup.exe` | Сол файлдары бар орнатушы |
| `*.sha256` | Әр ZIP пен орнатушының SHA-256 мәні |
| `Univer-Desktop-<нұсқа>-manifest.json`, `-installers.json` | Шығарылым файлдарының өлшемдері мен SHA-256 мәндері бар тізімі |

Буып-түю құрастырмадағы `Assets` бастапқы кодпен сәйкес келетінін, пакетке өзін-өзі тексеру файлдары, журналдар, `.pdb`, кіру деректері немесе браузер профилі түспегенін және онда тек `ru`, `en-GB`, `en-US` Chromium тілдері бар, WebGPU компиляторы жоқ екенін тексереді. Тексеру өтпесе, жаңа `-OutputRoot` бумасына қайта құрастырыңыз.

Орнатушыны Inno Setup 7.1.0 құрастырады: `scripts/prepare-installer-tools.ps1` оны ресми шығарылымнан `tools\` бумасына жүктеп, SHA-256 мен баспагер қолтаңбасын тексереді және жүйеге орнатпай ашады. Microsoft компоненттері орнатушыға салынбайды: олардың мекенжайлары, нұсқалары және SHA-256 мәндері `installer/prerequisites.json` ішінде бекітілген.

## Тексерулер

```powershell
dotnet restore .\Simple-UniverKazNU.sln --locked-mode
dotnet test .\tests\Univer.Desktop.Tests\Univer.Desktop.Tests.csproj -c Release
npm.cmd ci --ignore-scripts
npm.cmd test
.\scripts\test-localization.ps1
.\scripts\smoke.ps1 -Platform x64 -BrowserFlavor Modern -OutputRoot $build
.\scripts\smoke.ps1 -Platform x86 -BrowserFlavor Legacy -OutputRoot $build
.\scripts\smoke.ps1 -Platform x64 -BrowserFlavor Modern -OutputRoot $build -Live
.\scripts\test-release-packaging.ps1 -OutputRoot $build
.\scripts\test-build-release.ps1
```

`smoke.ps1` құрастырылған қолданбаны `--self-test` кілтімен бөлек уақытша профильде іске қосады: терезелер, синтетикалық беттерде кіру, жүктеулер, файлдарды қарау, тақырыптар, хабарландырулар, шығу. Ол нағыз аккаунтқа да, сіздің Univer профиліңізге де тиіспейді. `-Live` пішінді және кіру деректерін жібермей, `univer.kaznu.kz` ашық кіру бетін оқуды қосады. `-Only <қадам>` бір қадамды іске қосады (`settings`, `shell`, `document`, `presentation`, `form-download`, `layout-load`, `downloads`, `office`, `zoom`, `theme`, `notices`, `live`, `logout`). Шығарылатын әр нұсқа үшін smoke қайталаңыз.

Орнатушы тексерулері қолданбаны бөлек бумаға орнатып, жояды және бұрыннан орнатылған Univer-ге тиіспейді:

```powershell
.\scripts\smoke-installer.ps1 -ArtifactRoot $dist                     # орнату, қалпына келтіру, жою
.\scripts\test-installer-upgrade.ps1 -OldExe <алдыңғы нұсқаның Univer.Desktop.exe файлы> -ArtifactRoot $dist
.\scripts\test-installer-guards.ps1                                   # бөтен және желілік бумалар, junction, жаңаның үстінен ескі нұсқа, іске қосылған қолданба
.\scripts\test-installer-download.ps1                                 # Inno Setup SHA-256 мәні қате файлды қабылдамайды (желі қажет)
```

## Скрипттер

| Скрипт | Не істейді |
| --- | --- |
| `build-release.ps1` | Құрастыру, буып-түю, тексеру және орнатушы бір командамен |
| `build.ps1` | Нұсқаларды құрастыру (`-BuildOnly`), портативті буып-түю (`-SkipBuild`) немесе бәрі бірден |
| `build-installer.ps1` | Портативті мұрағаттар мен манифесттен орнатушылар |
| `prepare-installer-tools.ps1` | Inno Setup 7.1.0 нұсқасын `tools\` ішіне жүктеп, тексереді |
| `smoke.ps1` | Құрастырылған нұсқаның өзін-өзі тексеруі |
| `smoke-installer.ps1` | Дайын орнатушыны оқшау бумада орнату, қалпына келтіру және жою |
| `test-installer-guards.ps1`, `test-installer-upgrade.ps1`, `test-installer-download.ps1` | Орнатушы қорғаныстары, алдыңғы нұсқадан жаңарту, жүктеулердің SHA-256 тексеруі |
| `test-localization.ps1` | Интерфейс пен орнатушының барлық жолдары орыс, қазақ және ағылшын тілдерінде бар |
| `test-release-packaging.ps1`, `test-build-release.ps1` | Буып-түю, манифесттер, қайта жазуға тыйым және `.gitignore` ережелері шағын синтетикалық файлдарда |
| `update-pdfjs.ps1` | `Assets/pdfjs` бумасын бекітілген SHA-512 тексеруімен npm-нен жаңартады |
| `apply-signed-files.ps1` | SignPath файлдарының қолын тексереді, қол қойылмаған файлдарды ауыстырады және орнатушылардың SHA-256 мен манифестін жаңартады |

## Жаңа нұсқа

1. Нұсқаны `src/Univer.Desktop/Univer.Desktop.csproj` (`<Version>`), `src/Univer.Desktop/app.manifest` (`assemblyIdentity`, төрт бөлік) және `package.json` ішінде көтеріңіз, содан кейін `npm.cmd install --package-lock-only --ignore-scripts` іске қосыңыз. `VersionTests` тесті нұсқалардың сәйкестігін тексереді.
2. Барлық төрт нұсқаны құрастырып, жоғарыдағы тексерулерді әрқайсысы үшін өткізіңіз.
3. `vX.Y.Z` тегін қойыңыз (төменде қараңыз): GitHub ZIP, орнатушыларды, олардың `.sha256` файлдарын және екі манифестті өзі құрастырып тіркейді. Шығарылым сипаттамасында өзгерістерді және қай жүйелер тексерілгенін көрсетіңіз. Дайын файлдар репозиторий тарихына қосылмайды.

## GitHub-та шығару

Жария шығарылымдарды GitHub-тың таза машинасында `.github/workflows/release.yml` құрастырады. `vX.Y.Z` тегі жоба нұсқасымен сәйкес келуі керек: құрастыру тесттерді іске қосады, төрт нұсқаны құрастырады, олардың іске қосылуын тексереді, буып-түйеді және шығарылымды жариялайды. Қолмен іске қосу да осыны істейді, бірақ файлдарды тек 7 күн сақтайды.

Қол қою репозиторийде `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_POLICY` айнымалылары мен `SIGNPATH_API_TOKEN` құпиясы берілгенде қосылады. Сонда құрастыру SignPath-та қолмен растауды екі рет күтеді: алдымен `Univer.Desktop.exe` үшін, содан кейін ішінде қол қойылған қолданба бар орнатушылар үшін. Қол қою баптаулары `.signpath/artifact-configurations/` ішінде.

Орнатылған қолданба соңғы шығарылымнан `vX.Y.Z` тегі астындағы `Univer-Desktop-<нұсқа>-<түрі>-<разряд>-setup.exe` файлдарын және олардың `.sha256` файлдарын іздейді. Бұл атаулар өзгерсе, қолданбалар жаңартыла алмайды.

## Иесінің шолуына арналған құрастырма

Нағыз кабинетті аралаушы (`UI/Qa/MainForm.Review.cs` және көршілес файлдар) қалыпты құрастырмаға кірмейді. Ол тек анық және тек иесінің жергілікті шолуы үшін құрастырылады, ешкімге берілмейді:

```powershell
dotnet build .\src\Univer.Desktop\Univer.Desktop.csproj -c Release -p:Platform=x64 -p:BrowserFlavor=Modern -p:QaReview=true
```
