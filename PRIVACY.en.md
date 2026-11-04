[Русский](PRIVACY.md) · [Қазақша](PRIVACY.kz.md) · **English**

# Privacy policy

Simple Univer for Windows is an unofficial app for the [univer.kaznu.kz](https://univer.kaznu.kz/) website. The app has no server and no accounts of its own. The developer receives none of your data: no password, no grades, no usage information.

## What the app does not have

- analytics, telemetry, usage counters or advertising;
- crash report uploads;
- requests to the developer's servers;
- data sharing with third parties.

## Where the app connects

| Where | When and what |
| --- | --- |
| `https://univer.kaznu.kz` | Everything you do on the portal, as in an ordinary browser. In addition, the app itself signs in with the saved login and password through the site's form, reads portal sections in the background for notifications and distance courses (read only, at most once every 10 minutes) and downloads the files you chose |
| Your default browser | External links you clicked, and the move to Moodle from "СДО Moodle": the hand-off address is issued by the Univer site |
| Servers the site loads resources from | Univer pages may include images, videos or scripts from other sites; the site decides this, as in any browser |
| `github.com`, `*.githubusercontent.com` | The update check at start, at most once every 6 hours, and the installer download after you press Install. The check cannot be turned off. GitHub sees your IP address and the app's name; your login, password and portal data are not sent there. GitHub's rules: [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) |
| `download.microsoft.com`, `download.visualstudio.microsoft.com` | Only the installer, and only if the computer lacks .NET Framework or Visual C++ and you agreed to download them |

The built-in Chromium starts with component updates and logging turned off. A downloaded update installer stays in `%LOCALAPPDATA%\Univer.Desktop\updates` and is deleted at the next start; the last check's result is kept in the settings.

## What is stored on the computer

Everything is stored locally only, in your Windows account profile:

| What | Where | Contents |
| --- | --- | --- |
| Sign-in data | `%LOCALAPPDATA%\Univer.Desktop\credentials.dpapi` | Login and password encrypted with Windows DPAPI: only your Windows account on this computer can decrypt them |
| Settings | `%LOCALAPPDATA%\Univer.Desktop\preferences.json` | Language, theme, view, zoom, window size, tray, pinned links (up to 24), the "do not show the Legacy warning" mark, the automatic sign-in pause. No password |
| Downloads list | `%LOCALAPPDATA%\Univer.Desktop\downloads.json` | Names, paths, sizes and dates of downloaded files and the course a file came from; a hash is stored instead of the file's address. No cookies |
| Notifications | `%LOCALAPPDATA%\Univer.Desktop\notices.json` | Menu item titles, questionnaire names with deadlines, the site's message texts and "read" marks. No passwords or form values |
| Browser profile | `%LOCALAPPDATA%\Univer.Desktop\Browser-Modern` (Modern) or `Browser` (Legacy) | Session cookies, cache and storage of the Univer site, as in an ordinary browser |
| Downloaded files | Your Downloads folder | The files you downloaded |

There is no personal data in the program folder (installed or portable). The installer puts only `installation-id.txt` there, with the variant name (for example `Univer.Desktop.modern.x64`), so that an update lands in the same folder.

## How to delete data

- **Sign out** ("Settings → Account"): deletes the site's cookies and storage, replaces the browser profile and pauses automatic sign-in. The saved password stays until you remove it.
- **Remove saved login** ("Settings → Account"): deletes the encrypted record and ends the session; the notification list is cleared too.
- **Remove from list** in "Downloads": removes the entry; the file itself stays in the Downloads folder.
- **Delete everything**: uninstall the program through "Settings → Apps", then delete the `%LOCALAPPDATA%\Univer.Desktop` folder. Uninstalling does not touch this folder by itself, so that updates and reinstalls keep your sign-in.

Files are deleted the usual Windows way: there is no guaranteed erasure from the disk (especially SSDs) or from Windows backups. Signing out in the app does not end sessions on other devices.

## Your data on the site

Grades, schedule, questionnaires and other portal data are kept by the university on its website; the app only shows them. Al-Farabi KazNU is responsible for processing this data under its own rules.

## Changes

Changes to this policy are visible in the history of this file in the repository. Questions can be asked in the repository's issues; report vulnerabilities privately, as described in [SECURITY.en.md](SECURITY.en.md).
