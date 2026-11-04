[Русский](THIRD-PARTY.md) · [Қазақша](THIRD-PARTY.kz.md) · **English**

# Third-party components

The app's own code is distributed under GPL-3.0-or-later ([LICENSE](LICENSE)). The components below keep their own licenses; all of them are compatible with GPL version 3.

## In the app

- **CefSharp.WinForms / CefSharp.Common 152.0.150** (Modern): CEF 152.0.15+gb3d72fb, Chromium 152.0.7977.169. Project [cefsharp/CefSharp](https://github.com/cefsharp/CefSharp), BSD-3-Clause; [Chromium Embedded Framework](https://bitbucket.org/chromiumembedded/cef/), BSD-3-Clause. Licenses: `licenses/CefSharp-152-LICENSE.txt`, `licenses/CEF-152-LICENSE.txt`.
- **CefSharp.WinForms / CefSharp.Common 109.1.110** (Legacy): CEF 109.1.11, Chromium 109.0.5414.87, BSD-3-Clause. Licenses: `licenses/CefSharp-LICENSE.txt`, `licenses/CEF-LICENSE.txt`. Chromium 109 has known vulnerabilities, see [SECURITY.en.md](SECURITY.en.md).
- **Chromium runtime**, part of the CefSharp packages: ANGLE, SwiftShader and the Vulkan loader under open licenses (see the CEF licenses), and `d3dcompiler_47.dll`, Microsoft's closed-source DirectX system library needed for rendering. Windows 8.1 and later ship the same file.
- **System.Runtime.InteropServices.RuntimeInformation 4.3.0** (Modern, a CefSharp dependency), MIT. License: `licenses/RuntimeInformation-LICENSE.txt`, `licenses/RuntimeInformation-ThirdPartyNotices.txt`.
- **Mozilla PDF.js (pdfjs-dist) 5.7.284**, legacy build, Apache-2.0, [mozilla/pdf.js](https://github.com/mozilla/pdf.js): the PDF viewer in "Downloads". The files are in `src/Univer.Desktop/Assets/pdfjs` with their `LICENSE`; they are updated only by `scripts/update-pdfjs.ps1`, which checks the pinned SHA-512 of the npm package and writes `VERSION.txt`. PDF.js comes with its data, whose licenses lie next to it: Adobe CMaps (BSD-3-Clause, `cmaps/LICENSE`), PDFium/Foxit standard fonts (BSD-3-Clause, `standard_fonts/LICENSE_FOXIT`), the OpenJPEG (BSD-2-Clause), PDFium JBIG2 (BSD-3-Clause) and qcms (MIT) decoders in `wasm/`, the CGATS001 CMYK profile (CC0, `iccs/LICENSE`). The Liberation fonts and the QuickJS sandbox are not taken from the package. PDF.js 6.x does not run in Chromium 109, so the version is pinned to 5.7.

## Build and tests only

These components are not part of the ready-made app.

- **Microsoft.NETFramework.ReferenceAssemblies.net452 / net462 / net48 1.0.3**: [Microsoft Reference Assemblies](https://github.com/microsoft/dotnet/tree/main/releases/reference-assemblies).
- **xUnit 2.9.3**, runner 2.8.2, Microsoft.NET.Test.Sdk 17.11.1: tests, [xUnit](https://github.com/xunit/xunit).
- **jsdom 22.1.0** and its dependencies: page tests, versions and hashes in `package-lock.json`; [jsdom](https://github.com/jsdom/jsdom).
- **Inno Setup 7.1.0**: the installer compiler, [jrsoftware.org](https://jrsoftware.org/isinfo.php). It is downloaded during the build with a check of its SHA-256 and signature; its license is installed with the app as `licenses\Inno-Setup.txt`.

## Installed separately

.NET Framework and Visual C++ Redistributable are installed by the user (or by the installer with the user's consent) from official Microsoft addresses; they are not bundled with the app.

The sign-in automation script and the page layout adapter come from the Simple Univer Android app by the same authors. There is no real sign-in data and no real portal pages in the sources or releases.
