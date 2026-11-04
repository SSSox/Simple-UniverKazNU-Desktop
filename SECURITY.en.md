[Русский](SECURITY.md) · [Қазақша](SECURITY.kz.md) · **English**

# Security

## Reporting a vulnerability

Send a private report through the repository's **Security → Report a vulnerability** tab. Do not post details in public issues until a fix is out. Describe the version and variant (Modern or Legacy, x64 or x86), the steps and what should have happened. Do not send passwords, cookies or screenshots of someone else's account.

Fixes are released for the latest version only. The Legacy variant is built on Chromium 109, which gets no more security updates (see below): vulnerabilities of Chromium itself cannot be fixed in it.

## What the app does

**Works with one site only.** Automation, the saved password, session cookies and protected downloads are allowed only for the exact address `https://univer.kaznu.kz` (port 443, no login in the address). HTTP, other ports, subdomains and look-alike addresses get none of them. The rule is in `src/Univer.Desktop/Core/SitePolicy.cs`. Embedded frames from other sites get neither sign-in data nor the app's scripts.

**Does not weaken HTTPS checks.** Certificates are checked by Chromium and Windows; `ignore-certificate-errors` and "trust everything" are not used, and certificate errors are rejected. Legacy starts Chromium 109 with `disable-features=ChromeRootStoreUsed`: its built-in list of root certificates is out of date and rejects the site's current chain, so Windows certificate verification and the Windows certificate store are used instead. The chain, name and validity period are still checked.

**Stores the password encrypted.** The login and password are kept as one record, `credentials.dpapi`, encrypted with Windows DPAPI for the current user (`ProtectedData`, `CurrentUser`); the file is replaced atomically (`Core/Storage.cs`). The password is not in the sources, settings, logs or the program folder. Chromium logging is turned off.

**Signs in through the site's own form.** On `/user/login` the app fills the visible login and password fields and presses the site's button; there is no separate HTTP sign-in and no bypassing of the site's scripts. The site's re-sign-in dialog (`#auth_modal`) is confirmed with the site's own `/user/ajl` request, and the result is checked against the server's answer. If the site asks for the password of a different account, the app stops without typing the password. Attempts are limited: at most three sign-in form submissions in 10 minutes and three re-sign-ins a minute; after two rejections of the form in a row, automatic sign-in stops until the user acts. A form submission (POST) is never repeated automatically; after a network failure only the page (GET) is reloaded, with a growing pause from 5 seconds to 5 minutes.

**Confirms only two known dialogs.** With a saved sign-in the app ticks the checkbox and presses the site's button in two dialogs that block the portal after sign-in: the read notice (`#attentionSt`) and the additional agreement (`#modal_agreement`, the "прочитал договор" checkbox, `/user/setagree`). Each dialog is recognised by its exact markup and label and is confirmed once, without resubmission. The app submits or confirms no other forms, questionnaires, messages, requests or payments. The code is in `src/Univer.Desktop/Assets/univer-automation.js` and `Web/AutomationController.cs`.

**Reads pages in the background with GET only.** Notifications (`Assets/notices.js`) and distance-course tasks (`Assets/course-tasks.js`) read pages of the same site with `fetch` and the site's cookies, GET only, with time and size limits. A response from another address, a non-HTML response or a sign-in page is discarded. Nothing is clicked or submitted.

**Gives pages no access to the computer.** There are no JavaScript objects with access to files or the system. A page can only send the app short messages (load state, language, sign-in result), and they are accepted only from the main document of the trusted site and the current session. Chromium's remote debugging port is not opened.

**Checks downloads.** Course files (UMKD) are downloaded by a separate client (`Web/DownloadService.cs`, `Web/SessionHttp.cs`): cookies are sent only to the site, at most five redirects and only within the site, only a 200 response is accepted, a sign-in page or HTML instead of a file is rejected, files are limited to 1 GB. The file name is cleaned of invalid characters and paths, an existing file is never overwritten, and an incomplete `.part` file is deleted. Files are saved to the Downloads folder and are never opened by themselves: a document or image opens only when you click it, and the app never starts EXE, CMD or other executable files.

**Shows files without their programs.** The viewer for DOC, DOCX, XLS, XLSX, PPT, PPTX, HTML and text builds its own markup from the file contents (`Core/*Html.cs`, `Core/HtmlPreview.cs`). The viewer page is served with the CSP rule `default-src 'none'`: no scripts, frames or network. XML is read without DTD and external entities, with limits on size and depth. HTML is rebuilt from an allow-list: scripts, styles, forms, handlers and external images are removed. PDF is shown by Mozilla PDF.js from the program folder.

**Opens external links in the Windows browser.** Only `http`, `https`, `mailto` and `tel` are allowed; `file:`, `javascript:`, `shell:`, executable files and other protocols are blocked, and so are automatic redirects of the main page to other sites.

**Signs out completely.** Signing out first records the pause of automatic sign-in, then cancels downloads, deletes the site's cookies and storage and creates a new browser profile. Removing the saved login erases the encrypted record. This is a sign-out on this computer: the app does not promise that the server ends all sessions and does not promise guaranteed erasure of files from the disk.

**What the app touches in Windows.** It reads (never writes) three registry keys: the Windows theme (`HKCU\…\Themes\Personalize`), font substitutes (`HKLM\…\FontSubstitutes`) and file preview handlers (`HKCR`). Ctrl+mouse wheel is caught with a hook on its own thread only (`UI/CtrlWheel.cs`), without global hooks or DLLs. Processes are started only on a user action: opening a downloaded file, the Downloads folder or an external link (`UI/MainForm.Actions.cs`, `UI/MainForm.Downloads.cs`).

## Test code in the sources

`UI/Qa/` contains the self-test (`--self-test`), which is also present in the normal build: it runs on synthetic pages with a separate temporary profile and never opens the real portal. The crawler of the real portal used for the owner's review (`UI/Qa/MainForm.Review.cs`, `MainForm.Explore.cs`, `MainForm.ReviewFeatures.cs`) is behind `#if QA_REVIEW` and is built only explicitly with `-p:QaReview=true`; it is not part of releases, and the normal build contains empty stubs in its place.

## Installer

The Inno Setup installer (`installer/Univer.iss`) installs the app for the current user without administrator rights. It downloads missing .NET Framework and Visual C++ only after you confirm and only from the addresses pinned in `installer/prerequisites.json` (`download.microsoft.com` and `download.visualstudio.microsoft.com`), checks the pinned SHA-256 and never restarts the computer by itself. During the build, the Inno Setup 7.1.0 compiler is downloaded by `scripts/prepare-installer-tools.ps1`, which checks its SHA-256 and publisher signature. The installer refuses to install into a folder with other files or into the user's data folder.

## Known limitations

- **Legacy contains known Chromium 109 vulnerabilities** ([GHSA-f87w-3j5w-v58p](https://github.com/advisories/GHSA-f87w-3j5w-v58p)); NuGet warns about them during the build (`NU1903`). It is needed only for Windows 7 SP1/8/8.1; on Windows 10/11 use Modern. The absence of known vulnerabilities in Modern on the release date does not mean there are none.
- **Files are not signed** with a publisher certificate. Compare the SHA-256 from the `.sha256` file next to each release file.
- **DPAPI does not protect against programs already running as you.** The encrypted record may end up in Windows profile backups; there is no guaranteed erasure from SSDs or backups. The password stays in the app's memory while it is needed for sign-in.
- The site's own pages may load resources from other servers, as in any browser; the app does not add them.
- There are no automatic updates: look for new versions on the Releases page.

## Checking it yourself

All sources are in this repository; the build is described in [BUILD.en.md](BUILD.en.md). The app's network code is Chromium (the site's pages) and a single `HttpClient` in `Web/SessionHttp.cs` for downloads; searching `src/` for `HttpClient`, `WebRequest`, `Process.Start`, `Registry.` and `SetWindowsHookEx` shows every such place. Other addresses appear in the code only in the self-test's synthetic pages (a copy of the site's "Useful links") and as XML namespace names in Office documents. Privacy and stored data: [PRIVACY.en.md](PRIVACY.en.md).
