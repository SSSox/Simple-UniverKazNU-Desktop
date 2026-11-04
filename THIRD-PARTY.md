**Русский** · [Қазақша](THIRD-PARTY.kz.md) · [English](THIRD-PARTY.en.md)

# Сторонние компоненты

Код самого приложения распространяется по GPL-3.0-or-later ([LICENSE](LICENSE)). Компоненты ниже остаются под своими лицензиями; все они совместимы с GPL версии 3.

## В приложении

- **CefSharp.WinForms / CefSharp.Common 152.0.150** (Modern): CEF 152.0.15+gb3d72fb, Chromium 152.0.7977.169. Проект [cefsharp/CefSharp](https://github.com/cefsharp/CefSharp), BSD-3-Clause; [Chromium Embedded Framework](https://bitbucket.org/chromiumembedded/cef/), BSD-3-Clause. Лицензии: `licenses/CefSharp-152-LICENSE.txt`, `licenses/CEF-152-LICENSE.txt`.
- **CefSharp.WinForms / CefSharp.Common 109.1.110** (Legacy): CEF 109.1.11, Chromium 109.0.5414.87, BSD-3-Clause. Лицензии: `licenses/CefSharp-LICENSE.txt`, `licenses/CEF-LICENSE.txt`. В Chromium 109 есть известные уязвимости, см. [SECURITY.md](SECURITY.md).
- **Среда выполнения Chromium** входит в пакеты CefSharp: ANGLE, SwiftShader и загрузчик Vulkan с открытыми лицензиями (см. лицензии CEF) и `d3dcompiler_47.dll`, закрытая системная библиотека DirectX от Microsoft, нужная для отрисовки. Такой же файл есть в самой Windows 8.1 и новее.
- **System.Runtime.InteropServices.RuntimeInformation 4.3.0** (Modern, зависимость CefSharp), MIT. Лицензия: `licenses/RuntimeInformation-LICENSE.txt`, `licenses/RuntimeInformation-ThirdPartyNotices.txt`.
- **Mozilla PDF.js (pdfjs-dist) 5.7.284**, legacy-сборка, Apache-2.0, [mozilla/pdf.js](https://github.com/mozilla/pdf.js): просмотр PDF в «Загрузках». Файлы в `src/Univer.Desktop/Assets/pdfjs` с лицензией `LICENSE`; обновляются только скриптом `scripts/update-pdfjs.ps1`, который сверяет закреплённый SHA-512 пакета из npm и пишет `VERSION.txt`. Вместе с PDF.js поставляются его данные, лицензии лежат рядом с ними: CMap Adobe (BSD-3-Clause, `cmaps/LICENSE`), стандартные шрифты PDFium/Foxit (BSD-3-Clause, `standard_fonts/LICENSE_FOXIT`), декодеры OpenJPEG (BSD-2-Clause), JBIG2 из PDFium (BSD-3-Clause) и qcms (MIT) в `wasm/`, CMYK-профиль CGATS001 (CC0, `iccs/LICENSE`). Шрифты Liberation и песочница QuickJS из пакета не берутся. PDF.js 6.x не работает в Chromium 109, поэтому версия закреплена на 5.7.

## Только для сборки и тестов

Эти компоненты не входят в готовое приложение.

- **Microsoft.NETFramework.ReferenceAssemblies.net452 / net462 / net48 1.0.3**: [Microsoft Reference Assemblies](https://github.com/microsoft/dotnet/tree/main/releases/reference-assemblies).
- **xUnit 2.9.3**, runner 2.8.2, Microsoft.NET.Test.Sdk 17.11.1: тесты, [xUnit](https://github.com/xunit/xunit).
- **jsdom 22.1.0** и его зависимости: тесты страниц, версии и хеши в `package-lock.json`; [jsdom](https://github.com/jsdom/jsdom).
- **Inno Setup 7.1.0**: компилятор установщика, [jrsoftware.org](https://jrsoftware.org/isinfo.php). Скачивается при сборке с проверкой SHA-256 и подписи; его лицензия ставится вместе с приложением в `licenses\Inno-Setup.txt`.

## Устанавливаются отдельно

.NET Framework и Visual C++ Redistributable ставит пользователь (или установщик с его согласия) с официальных адресов Microsoft; в приложение они не вложены.

Скрипт автоматизации входа и адаптер вида страниц перенесены из Android-приложения Simple Univer того же автора. Настоящих данных входа и страниц кабинета в исходниках и выпусках нет.
