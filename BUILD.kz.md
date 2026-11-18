[Русский](BUILD.md) · **Қазақша** · [English](BUILD.en.md)

# Құрастыру, тексеру және шығару

Қолданбаны осы бастапқы кодтан құрастыруға қажеттінің бәрі: портативті мұрағаттар, орнатушылар және тексерулер. Дайын қолданбаны пайдаланушыларға мұның ешқайсысы қажет емес.

## Не қажет

- Windows 10 немесе 11.
- [.NET SDK](https://dotnet.microsoft.com/download) (10.0.400 нұсқасында тексерілген). Қолданба .NET Framework 4.5.2 (Legacy) және 4.6.2 (Modern) үшін, тесттер 4.8 үшін құрастырылады; қажетті reference assemblies NuGet-тен келеді.
- PowerShell 7 (`pwsh`); 7.6 нұсқасында тексерілген.
- Node.js және npm тек бет тесттері (`tests/dom`) мен кеңейтуді тексеру (`tests/extension`) үшін.
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

`smoke.ps1` құрастырылған қолданбаны `--self-test` кілтімен бөлек уақытша профильде іске қосады: терезелер, синтетикалық беттерде кіру, жүктеулер, файлдарды қарау, тақырыптар, хабарландырулар, шығу. Ол нағыз аккаунтқа да, сіздің Univer профиліңізге де тиіспейді. `-Live` пішінді және кіру деректерін жібермей, `univer.kaznu.kz` ашық кіру бетін оқуды қосады. `-Only <қадам>` бір қадамды іске қосады (`settings`, `shell`, `document`, `presentation`, `form-download`, `layout-load`, `downloads`, `office`, `zoom`, `theme`, `notices`, `live`, `logout`).

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
| `build-extension.ps1` | `src/Univer.Extension` пен `Assets/` бет скрипттерінен Chrome, Edge және Firefox кеңейтуін жасайды |
| `update-pdfjs.ps1` | `Assets/pdfjs` бумасын бекітілген SHA-512 тексеруімен npm-нен жаңартады |
| `apply-signed-files.ps1` | SignPath файлдарының қолын тексереді, қол қойылмаған файлдарды ауыстырады және орнатушылардың SHA-256 мен манифестін жаңартады |

## Браузер кеңейтуі

```powershell
.\scripts\build-extension.ps1          # artifacts\extension\{chromium,firefox}, екі ZIP және олардың .sha256 файлдары
.\scripts\build-extension.ps1 -Qa      # жергілікті синтетикалық беттер үшін де, ZIP-сіз
node tests\extension\chrome.mjs --ext artifacts\extension-qa\chromium --out <бума>
node tests\extension\firefox.mjs --ext artifacts\extension-qa\firefox --out <бума>
```

Құрастыру бет скрипттерінің тізімін `Web/AutomationController.cs` файлынан, ал файлдардың өзін `Assets/` бумасынан өзгертпей алады, әрқайсысы өз қатаң қабығында; кеңейтудің өз скрипттері `src/Univer.Extension/` бумасында. Қалыпты құрастыру тек `https://univer.kaznu.kz` сайтында жұмыс істейді, QA құрастыруы `tests/extension/pages.mjs` синтетикалық беттері үшін `127.0.0.1` мекенжайында да. `chrome.mjs` кеңейтуді портты ашпай DevTools pipe арқылы Chrome-ның уақытша профиліне жүктейді, `firefox.mjs` оны WebDriver BiDi арқылы Firefox-тың бөлек профиліне уақытша қосымша ретінде орнатады; `--live` деректер енгізбей жария кіру бетін қосады. `npm.cmd test` кеңейту скрипттерін jsdom-да және қалыпты құрастыруды тексереді (`tests/dom/extension-build.test.cjs`: жалғыз `storage` рұқсаты, бір сайт, бет скрипттерінің байттары `Assets/` бумасымен бірдей).

## Шығарылымдар

Жария шығарылымдарды GitHub-тың таза машинасында `.github/workflows/release.yml` құрастырады: `vX.Y.Z` тегі бойынша ол тесттерді іске қосады, төрт нұсқаны құрастырады, олардың іске қосылуын тексереді және файлдарды `.sha256` сомаларымен бірге жариялайды.
