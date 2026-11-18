**Русский** · [Қазақша](BUILD.kz.md) · [English](BUILD.en.md)

# Сборка, проверка и выпуск

Всё, что нужно, чтобы собрать приложение из этих исходников: portable-архивы, установщики и проверки. Пользователям готового приложения ничего из этого не нужно.

## Что нужно

- Windows 10 или 11.
- [.NET SDK](https://dotnet.microsoft.com/download) (проверен 10.0.400). Приложение собирается под .NET Framework 4.5.2 (Legacy) и 4.6.2 (Modern), тесты под 4.8; нужные reference assemblies приходят из NuGet.
- PowerShell 7 (`pwsh`); проверено с 7.6.
- Node.js и npm только для тестов страниц (`tests/dom`) и проверки расширения (`tests/extension`).
- Git только для `test-release-packaging.ps1`.
- Интернет при первой сборке: пакеты NuGet и компилятор установщика.

Пакеты NuGet и npm закреплены lock-файлами (`packages.lock.json`, `packages.modern.lock.json`, `package-lock.json`); восстановление идёт в режиме `--locked-mode`, версии без необходимости не меняйте.

## Одна команда

Из корня репозитория в PowerShell 7:

```powershell
.\scripts\build-release.ps1 -Package Portable                 # portable Modern x64
.\scripts\build-release.ps1 -Package Installer                # установщик Modern x64 (и его portable-основа)
.\scripts\build-release.ps1 -Package Both -BrowserFlavor all -Platform all -Verify
```

Каждый запуск собирает свежий build в `build\release-<дата>-<id>` и кладёт результат в новую папку `artifacts\release-<дата>-<id>` (или в `-OutputDirectory`). `-Verify` полностью распаковывает каждый ZIP и запускает из него самопроверку до создания установщика. `-BrowserFlavor` принимает `Modern`, `Legacy` или `all`, `-Platform`: `x64`, `x86` или `all`. При первой ошибке выпуск останавливается.

## По шагам

```powershell
$build = '.\build\local'
$dist = '.\artifacts\local-' + (Get-Date -Format 'yyyyMMdd-HHmmss')

# 1. Файлы приложения для всех четырёх вариантов, без архивов
.\scripts\build.ps1 -BuildOnly -OutputRoot $build
# 2. Portable: ZIP, .sha256 и манифест
.\scripts\build.ps1 -SkipBuild -OutputRoot $build -ArtifactRoot $dist
# 3. Установщики из этих ZIP
.\scripts\build-installer.ps1 -ArtifactRoot $dist
```

Для одного варианта передайте одинаковые `-BrowserFlavor` и `-Platform` во все три команды (например, `-BrowserFlavor Modern -Platform x64`). Скрипты не перезаписывают существующие ZIP и установщики: для повторной упаковки той же версии задайте новую папку `$dist`.

Legacy собирается с CefSharp 109; NuGet при этом показывает предупреждение `NU1903` об известной уязвимости Chromium 109. Его не подавляют: оно правдиво.

## Что получается

| Файл | Что это |
| --- | --- |
| `$build\<modern\|legacy>\<x64\|x86>\Univer.Desktop.exe` | Собранное приложение с соседними DLL, ресурсами Chromium и `Assets` |
| `Univer-Desktop-<версия>-<вариант>-<arch>.zip` | Portable-архив: приложение, `LICENSE`, `README`, `SECURITY`, `PRIVACY`, `BUILD` и `THIRD-PARTY` на трёх языках (`.md`, `.kz.md`, `.en.md`), `licenses/`, `BUILD-INFO.txt` |
| `Univer-Desktop-<версия>-<вариант>-<arch>-setup.exe` | Установщик с теми же файлами |
| `*.sha256` | SHA-256 каждого ZIP и установщика |
| `Univer-Desktop-<версия>-manifest.json`, `-installers.json` | Список файлов выпуска с размерами и SHA-256 |

Установленное приложение и сайт загрузок находят файлы выпуска по этим именам: если их изменить, приложения не смогут обновиться.

Упаковка проверяет, что `Assets` в сборке совпадают с исходниками, что в пакет не попали файлы самопроверки, журналы, `.pdb`, данные входа или профиль браузера, и что в нём только языки Chromium `ru`, `en-GB`, `en-US` без WebGPU-компилятора. Если проверка не прошла, пересоберите в новую папку `-OutputRoot`.

Установщик собирает Inno Setup 7.1.0: `scripts/prepare-installer-tools.ps1` скачивает его в `tools\` с официального выпуска, сверяет SHA-256 и подпись издателя и распаковывает без установки в систему. Компоненты Microsoft в установщик не вкладываются: их адреса, версии и SHA-256 закреплены в `installer/prerequisites.json`.

## Проверки

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

`smoke.ps1` запускает собранное приложение с ключом `--self-test` в отдельном временном профиле: окна, вход на синтетических страницах, скачивания, просмотр файлов, темы, уведомления, выход. Ни настоящий аккаунт, ни ваш профиль Univer он не трогает. `-Live` добавляет чтение публичной страницы входа `univer.kaznu.kz` без отправки формы и данных входа. `-Only <шаг>` запускает один шаг (`settings`, `shell`, `document`, `presentation`, `form-download`, `layout-load`, `downloads`, `office`, `zoom`, `theme`, `notices`, `live`, `logout`).

Проверки установщика ставят и удаляют приложение в отдельной папке и не трогают уже установленный Univer:

```powershell
.\scripts\smoke-installer.ps1 -ArtifactRoot $dist                     # установка, восстановление, удаление
.\scripts\test-installer-upgrade.ps1 -OldExe <Univer.Desktop.exe прошлой версии> -ArtifactRoot $dist
.\scripts\test-installer-guards.ps1                                   # чужие и сетевые папки, junction, старая версия поверх новой, запущенное приложение
.\scripts\test-installer-download.ps1                                 # Inno Setup отвергает файл с неверной SHA-256 (нужна сеть)
```

## Скрипты

| Скрипт | Что делает |
| --- | --- |
| `build-release.ps1` | Сборка, упаковка, проверка и установщик одной командой |
| `build.ps1` | Сборка вариантов (`-BuildOnly`), упаковка portable (`-SkipBuild`) или всё сразу |
| `build-installer.ps1` | Установщики из portable-архивов и манифеста |
| `prepare-installer-tools.ps1` | Скачивает и проверяет Inno Setup 7.1.0 в `tools\` |
| `smoke.ps1` | Самопроверка собранного варианта |
| `smoke-installer.ps1` | Установка, восстановление и удаление готового установщика в изолированной папке |
| `test-installer-guards.ps1`, `test-installer-upgrade.ps1`, `test-installer-download.ps1` | Защиты установщика, обновление с прошлой версии, проверка SHA-256 загрузок |
| `test-localization.ps1` | Все строки интерфейса и установщика есть на русском, казахском и английском |
| `test-release-packaging.ps1`, `test-build-release.ps1` | Упаковка, манифесты, запрет перезаписи и правила `.gitignore` на небольших синтетических файлах |
| `build-extension.ps1` | Расширение для Chrome, Edge и Firefox из `src/Univer.Extension` и скриптов страниц `Assets/` |
| `update-pdfjs.ps1` | Обновляет `Assets/pdfjs` из npm с проверкой закреплённого SHA-512 |
| `apply-signed-files.ps1` | Проверяет подпись файлов из SignPath, заменяет ими неподписанные и обновляет SHA-256 и манифест установщиков |

## Расширение для браузера

```powershell
.\scripts\build-extension.ps1          # artifacts\extension\{chromium,firefox}, два ZIP и их .sha256
.\scripts\build-extension.ps1 -Qa      # ещё и для локальных синтетических страниц, без ZIP
node tests\extension\chrome.mjs --ext artifacts\extension-qa\chromium --out <папка>
node tests\extension\firefox.mjs --ext artifacts\extension-qa\firefox --out <папка>
```

Сборка берёт список скриптов страниц из `Web/AutomationController.cs`, а сами файлы из `Assets/` без изменений, каждый в своей строгой обёртке; свои скрипты расширения лежат в `src/Univer.Extension/`. Обычная сборка работает только на `https://univer.kaznu.kz`, QA-сборка ещё и на `127.0.0.1` для синтетических страниц `tests/extension/pages.mjs`. `chrome.mjs` загружает расширение во временный профиль Chrome через DevTools pipe без открытого порта, `firefox.mjs` ставит его временным дополнением в отдельный профиль Firefox через WebDriver BiDi; `--live` добавляет публичную страницу входа без ввода данных. `npm.cmd test` проверяет скрипты расширения на jsdom и обычную сборку (`tests/dom/extension-build.test.cjs`: одно разрешение `storage`, один сайт, байты скриптов страниц совпадают с `Assets/`).

## Выпуски

Публичные выпуски собирает `.github/workflows/release.yml` на чистой машине GitHub: по тегу `vX.Y.Z` он запускает тесты, собирает четыре варианта, проверяет их запуск и публикует файлы вместе с их `.sha256`.
