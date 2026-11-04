[Русский](THIRD-PARTY.md) · **Қазақша** · [English](THIRD-PARTY.en.md)

# Үшінші тарап компоненттері

Қолданбаның өз коды GPL-3.0-or-later ([LICENSE](LICENSE)) бойынша таратылады. Төмендегі компоненттер өз лицензияларында қалады; олардың барлығы GPL 3-нұсқасымен үйлесімді.

## Қолданбада

- **CefSharp.WinForms / CefSharp.Common 152.0.150** (Modern): CEF 152.0.15+gb3d72fb, Chromium 152.0.7977.169. [cefsharp/CefSharp](https://github.com/cefsharp/CefSharp) жобасы, BSD-3-Clause; [Chromium Embedded Framework](https://bitbucket.org/chromiumembedded/cef/), BSD-3-Clause. Лицензиялар: `licenses/CefSharp-152-LICENSE.txt`, `licenses/CEF-152-LICENSE.txt`.
- **CefSharp.WinForms / CefSharp.Common 109.1.110** (Legacy): CEF 109.1.11, Chromium 109.0.5414.87, BSD-3-Clause. Лицензиялар: `licenses/CefSharp-LICENSE.txt`, `licenses/CEF-LICENSE.txt`. Chromium 109-да белгілі осалдықтар бар, [SECURITY.kz.md](SECURITY.kz.md) қараңыз.
- **Chromium орындау ортасы** CefSharp пакеттеріне кіреді: ашық лицензиялы ANGLE, SwiftShader және Vulkan жүктегіші (CEF лицензияларын қараңыз), сондай-ақ `d3dcompiler_47.dll`: суретті шығаруға қажет Microsoft-тың жабық DirectX жүйелік кітапханасы. Windows 8.1 және одан жаңа нұсқаларда дәл осындай файл бар.
- **System.Runtime.InteropServices.RuntimeInformation 4.3.0** (Modern, CefSharp тәуелділігі), MIT. Лицензия: `licenses/RuntimeInformation-LICENSE.txt`, `licenses/RuntimeInformation-ThirdPartyNotices.txt`.
- **Mozilla PDF.js (pdfjs-dist) 5.7.284**, legacy құрастырмасы, Apache-2.0, [mozilla/pdf.js](https://github.com/mozilla/pdf.js): «Жүктеулер» ішінде PDF қарау. Файлдар `LICENSE` лицензиясымен бірге `src/Univer.Desktop/Assets/pdfjs` бумасында; оларды тек `scripts/update-pdfjs.ps1` скрипті жаңартады, ол npm пакетінің бекітілген SHA-512 мәнін тексеріп, `VERSION.txt` жазады. PDF.js-пен бірге оның деректері жеткізіледі, лицензиялары жанында: Adobe CMap (BSD-3-Clause, `cmaps/LICENSE`), PDFium/Foxit стандартты қаріптері (BSD-3-Clause, `standard_fonts/LICENSE_FOXIT`), `wasm/` ішіндегі OpenJPEG (BSD-2-Clause), PDFium JBIG2 (BSD-3-Clause) және qcms (MIT) декодерлері, CGATS001 CMYK профилі (CC0, `iccs/LICENSE`). Liberation қаріптері мен QuickJS құмсалғышы пакеттен алынбайды. PDF.js 6.x Chromium 109-да жұмыс істемейді, сондықтан нұсқа 5.7-де бекітілген.

## Тек құрастыру мен тесттер үшін

Бұл компоненттер дайын қолданбаға кірмейді.

- **Microsoft.NETFramework.ReferenceAssemblies.net452 / net462 / net48 1.0.3**: [Microsoft Reference Assemblies](https://github.com/microsoft/dotnet/tree/main/releases/reference-assemblies).
- **xUnit 2.9.3**, runner 2.8.2, Microsoft.NET.Test.Sdk 17.11.1: тесттер, [xUnit](https://github.com/xunit/xunit).
- **jsdom 22.1.0** және оның тәуелділіктері: бет тесттері, нұсқалар мен хештер `package-lock.json` ішінде; [jsdom](https://github.com/jsdom/jsdom).
- **Inno Setup 7.1.0**: орнатушы компиляторы, [jrsoftware.org](https://jrsoftware.org/isinfo.php). Құрастыру кезінде SHA-256 және қолтаңба тексеруімен жүктеледі; оның лицензиясы қолданбамен бірге `licenses\Inno-Setup.txt` ретінде орнатылады.

## Бөлек орнатылады

.NET Framework және Visual C++ Redistributable компоненттерін пайдаланушы (немесе оның келісімімен орнатушы) Microsoft-тың ресми мекенжайларынан орнатады; олар қолданбаға салынбаған.

Кіруді автоматтандыру скрипті мен бет көрінісінің адаптері сол авторлардың Android-тағы Simple Univer қолданбасынан алынған. Бастапқы кодта және шығарылымдарда нағыз кіру деректері мен кабинет беттері жоқ.
