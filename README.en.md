[Русский](README.md) · [Қазақша](README.kz.md) · **English**

# Simple Univer for Windows

> **Unofficial app.** It is not affiliated with or supported by Al-Farabi Kazakh National University. Sign-in, data and forms stay on [univer.kaznu.kz](https://univer.kaznu.kz/): the app opens the real website in a built-in Chromium browser and makes it easier to use on a computer.

A desktop app for the [Univer KazNU](https://univer.kaznu.kz/) student portal with the same features as the Simple Univer Android app. The interface is built for mouse and keyboard and is available in Russian, Kazakh and English.

- **Saved sign-in.** Your login and password are stored encrypted on this computer; when the session ends, the app signs in again by itself.
- **Reading mode for the site:** readable tables and cards, one header instead of three bars, an "All sections" panel with search, pinned links on the home page. "View → Original layout" shows the site as it is.
- **Schedule** with the current and next class, a day filter and a week grid.
- **Grades** by subject, clear assessment pages, all distance-course tasks on one page.
- **Site notifications** about questionnaires and tests in the bell on the toolbar; questionnaires you have already answered are not counted.
- **"Downloads" (Ctrl+J):** every downloaded file with search and filters, and a built-in viewer for PDF, Word, Excel, PowerPoint, web pages, text and images, without Office.
- **Ctrl+F search** on site pages and in open files, news with search and filters.
- Light and dark theme, zoom, printing, running from the notification area (tray).

## Which file to download

Ready-made files are on the [Releases](../../releases) page. Every version comes in four variants, each in two forms: an installer (`-setup.exe`) and a portable archive (`.zip`).

| Your Windows | Variant | Installer | Portable |
| --- | --- | --- | --- |
| Windows 10 / 11, 64-bit | Modern x64 | `Univer-Desktop-<version>-modern-x64-setup.exe` | `Univer-Desktop-<version>-modern-x64.zip` |
| Windows 10, 32-bit | Modern x86 | `Univer-Desktop-<version>-modern-x86-setup.exe` | `Univer-Desktop-<version>-modern-x86.zip` |
| Windows 7 SP1 / 8 / 8.1, 64-bit | Legacy x64 | `Univer-Desktop-<version>-legacy-x64-setup.exe` | `Univer-Desktop-<version>-legacy-x64.zip` |
| Windows 7 SP1 / 8 / 8.1, 32-bit | Legacy x86 | `Univer-Desktop-<version>-legacy-x86-setup.exe` | `Univer-Desktop-<version>-legacy-x86.zip` |

You can see whether your Windows is 32- or 64-bit in "Settings → System → About → System type". If unsure, take Modern x64 on Windows 10/11.

**Modern or Legacy.** Modern runs on current Chromium 152 (CefSharp 152.0.150) and is meant for Windows 10 and 11. Legacy exists only for Windows 7 SP1, 8 and 8.1: Chromium 110 and later do not run on these systems, so Legacy uses Chromium 109 (CefSharp 109.1.110). **Chromium 109 has known vulnerabilities and gets no more updates** ([GHSA-f87w-3j5w-v58p](https://github.com/advisories/GHSA-f87w-3j5w-v58p)). The app reminds you of this at startup and in its settings. Do not use Legacy on Windows 10/11.

**Installer or portable.** The installer puts the app into the current user's profile, creates shortcuts and an uninstall entry in the Windows app list, and offers to download missing Microsoft components. The portable archive installs nothing: unpack it into any folder and run it from there. Both have the same features.

## Checking the downloaded file

Every file in a release has a `.sha256` file next to it with its SHA-256 checksum. Compare it with the checksum of the downloaded file:

```powershell
Get-FileHash .\Univer-Desktop-<version>-modern-x64-setup.exe
```

The `Hash` value must match the line in `.sha256`. If it does not, do not run the file.

The files are **not signed** with a publisher certificate, so Windows SmartScreen may show "Windows protected your PC" or "Unknown publisher". Run only a file whose checksum matched: "More info → Run anyway".

## Installation

1. Run the `-setup.exe` of the variant you need. Administrator rights are not required: the app goes to `%LOCALAPPDATA%\Programs\Univer\<variant>` (for example `modern-x64`).
2. Choose the installer language (Russian, Kazakh or English). The app's own language is chosen separately, in its settings.
3. If the computer lacks the required .NET Framework or Visual C++ Redistributable, the installer lists them and asks whether to download them. The files come only from official Microsoft addresses, are checked against a pinned SHA-256 and run after you confirm; Windows may ask for administrator rights to install them. If a restart is needed afterwards, restart Windows and run the Univer installer again.
4. A shortcut is created in the Start menu; a desktop shortcut can be enabled during installation.

The installer checks the system before copying files: Modern installs on Windows 10 and later, Legacy on Windows 7 SP1 and later, x64 only on 64-bit Windows. ARM64 is not supported.

**Silent installation** (the Microsoft components must already be installed): `Univer-Desktop-<version>-modern-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /TASKS=""`.

## Portable, without installation

1. Install the Microsoft components if you do not have them yet (table below).
2. Unpack the ZIP **completely** into a separate folder. `Univer.Desktop.exe` alone, without the DLLs next to it, the `Assets` and `locales` folders and the Chromium resources, will not start.
3. Run `Univer.Desktop.exe`.

Sign-in data and settings are kept in your Windows profile (`%LOCALAPPDATA%\Univer.Desktop`), not in the program folder, so the portable folder can be moved or deleted.

## Requirements

| Variant | .NET Framework | Visual C++ Redistributable |
| --- | --- | --- |
| Modern x64 / x86 | 4.6.2 or later (usually already present on Windows 10/11) | 2015–2022 (14.44 or later): [x64](https://aka.ms/vs/17/release/vc_redist.x64.exe) / [x86](https://aka.ms/vs/17/release/vc_redist.x86.exe) |
| Legacy x64 / x86 | [4.5.2](https://www.microsoft.com/en-us/download/details.aspx?id=42642) or later (on Windows 8.0 only 4.5.2: 4.8 does not support it) | 2015–2019 (14.29 or later): [x64](https://aka.ms/vs/16/release/vc_redist.x64.exe) / [x86](https://aka.ms/vs/16/release/vc_redist.x86.exe) |

The Visual C++ package must match the variant, not only Windows: Modern x86 needs the x86 package even on 64-bit Windows. Old Windows 7/8 may need the TLS 1.2, SHA-2 and root certificate updates for HTTPS; the app does not turn off certificate checks to work around their absence.

## Updating and uninstalling

The installed app checks GitHub by itself at start, at most once every 6 hours. When a new version is out, a window offers Later and Install and a "Don't show again" checkbox, which hides the window for that version only. The check always runs; its result and the same buttons are in Settings → Updates. After Install, Univer downloads the installer of its variant, verifies the SHA-256, closes, installs the new version and opens again.

You can also update by hand: close Univer (in the tray too) and run the installer of the new version of **the same variant** over the old one. Sign-in, settings and the downloads list are kept. The installer does not let you install an older version over a newer one. Modern, Legacy, x64 and x86 go into different folders and do not interfere with each other, but two variants cannot run at the same time.

Portable also tells you about a new version but does not update itself: the button opens the release page. Unpack the new archive into a new folder and delete the old one.

Uninstalling: "Settings → Apps → Univer → Uninstall". The program is removed, while its data stays in `%LOCALAPPDATA%\Univer.Desktop` and downloaded files stay in your Downloads folder. To remove everything:

1. Before uninstalling, open "Settings → Account → Remove saved login".
2. After uninstalling, delete the `%LOCALAPPDATA%\Univer.Desktop` folder.

## First start

On the first start the app offers two ways to sign in: enter your Univer login and password in the app window (they are then saved encrypted and sign-in happens by itself), or sign in manually on the site page without saving anything. After that the app reuses the saved session.

- "Settings → Account → Sign out" ends the session and clears cookies but keeps the saved password; automatic sign-in pauses until you press "Sign in again".
- "Remove saved login" erases the saved record and ends the session.
- If your password changed on the site, the app stops after two rejections in a row and asks for the new one.

With a saved sign-in the app itself confirms only two site dialogs that would otherwise block the portal: the read notice after sign-in and the additional agreement dialog with the "прочитал договор" (I have read the contract) checkbox. In both cases it ticks the checkbox and presses the site's own button. The app submits no other forms and confirms nothing else. Details: [SECURITY.en.md](SECURITY.en.md).

## Keyboard shortcuts

| Action | Keys |
| --- | --- |
| Back / close the open document | Alt+←, Esc |
| "Downloads" | Ctrl+J |
| Search on the page, in a document or file | Ctrl+F |
| Next / previous match | F3 or Enter / Shift+F3 or Shift+Enter |
| Reload the page (forms are never resent) | F5 |
| Settings | Ctrl+, |
| Print | Ctrl+P |
| Zoom 100% / in / out | Ctrl+0 / Ctrl+plus / Ctrl+minus, Ctrl+mouse wheel |

## What has been tested

The app was tested on Windows 11 x64, including the 32-bit builds on the same system. It was not tested on Windows 7, 8, 8.1 and 10, on a separate 32-bit Windows, on ARM64, with non-standard display scaling or on several monitors. It depends on the site: if the university changes its pages, parts of the reading mode may stop working until the app is updated; "Original layout" shows the site unchanged.

## Privacy and security

The app has no server of its own, no analytics, telemetry or crash reports. Its own requests go only to `https://univer.kaznu.kz` and to GitHub, where updates come from; external links open in your browser. The password is stored on this computer, encrypted with Windows DPAPI for your account.

- [PRIVACY.en.md](PRIVACY.en.md): what data is stored, where, and how to delete it.
- [SECURITY.en.md](SECURITY.en.md): what the app does and does not do, its limitations and how to report a vulnerability.

## Code signing policy

Release files are not signed yet. The project is applying for free code signing through [SignPath Foundation](https://signpath.org).

Only files that GitHub Actions builds from this repository ([`release.yml`](.github/workflows/release.yml)) will be signed: `Univer.Desktop.exe` and the installers. Chromium and CefSharp files and the Inno Setup uninstaller are third-party components and do not get this signature.

- Authors and reviewers: [SSSox](https://github.com/SSSox).
- Approver of every signature: [SSSox](https://github.com/SSSox).
- Privacy: [PRIVACY.en.md](PRIVACY.en.md).

## How the app works

It is a C# WinForms app (.NET Framework, C# 7.3) with Chromium built in through [CefSharp](https://github.com/cefsharp/CefSharp). The site opens as in an ordinary browser; the app adds its own layout and sign-in scripts to its pages and shows its own windows around it.

| Folder | Contents |
| --- | --- |
| `src/Univer.Desktop/Program.cs` | Startup, Chromium settings |
| `src/Univer.Desktop/Core/` | Rules: which address is trusted (`SitePolicy.cs`), data storage and encryption (`Storage.cs`), file names, document converters for the viewer, notifications |
| `src/Univer.Desktop/Web/` | Chromium handlers, download checks (`DownloadService.cs`, `SessionHttp.cs`), sign-in automation (`AutomationController.cs`) |
| `src/Univer.Desktop/UI/` | Windows, settings, "Downloads", translations (`Strings.cs`); `UI/Qa/`: built-in self-test on synthetic pages |
| `src/Univer.Desktop/Assets/` | JavaScript added to the site's pages: layout, sign-in, schedule, notifications; `pdfjs/`: Mozilla PDF.js for viewing PDF |
| `tests/` | xUnit tests for the C# code and jsdom page tests; no real accounts |
| `scripts/` | Build, packaging, installer and checks ([BUILD.en.md](BUILD.en.md)) |
| `installer/` | Inno Setup installer source and pinned Microsoft components |
| `licenses/` | Third-party licenses ([THIRD-PARTY.en.md](THIRD-PARTY.en.md)) |

## Building from source

You need Windows 10/11, the .NET SDK (tested with 10.0.400) and PowerShell 7. Users of the ready-made app do not need them.

```powershell
# Portable Modern x64 into a new artifacts\release-<date> folder
.\scripts\build-release.ps1 -Package Portable
# All four variants, portable and installers, with a check of the unpacked archives
.\scripts\build-release.ps1 -Package Both -BrowserFlavor all -Platform all -Verify
```

Tests, the step-by-step build, the installer and a description of every script: [BUILD.en.md](BUILD.en.md).

## License

Copyright (C) 2026 the Univer · KazNU authors.

The code is distributed under the [GNU General Public License](LICENSE), version 3 or, at your option, any later version (GPL-3.0-or-later). You may use, modify and distribute it, including for a fee, but a modified version of the app must be released under the same license together with its source code. The source code of every version is the release tag in this repository; a source archive is on the release page.

Third-party components (CefSharp, Chromium Embedded Framework, Mozilla PDF.js) keep their own licenses, listed in [THIRD-PARTY.en.md](THIRD-PARTY.en.md); the texts are in `licenses/` and next to the PDF.js files. The name, logo and materials of Al-Farabi KazNU are not covered by the license and belong to the university.
