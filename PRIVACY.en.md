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
| `https://univer.kaznu.kz` | Everything you do on the portal, as in an ordinary browser. In addition, the app itself signs in with the saved login and password through the site's form, reads portal sections in the background for notifications and distance courses (read only, at most once every 10 minutes), on the schedule page reads the "My account" page (or, failing that, the study plan), course materials, syllabi and the schedule of the semester's other weeks (read only; found plans are not reread for the rest of the semester or until Refresh, courses without a matching syllabus, the programme and the course list are reread every 1–3 days, the group's weeks every 7 days), reads the Current attestation page every 30 minutes (15, 30, 60 or 120 in Settings) for new grade notifications (read only; each check extends the site session), reads the neighbouring week of the schedule when you press «To calendar» or «As picture» on the schedule page (read only) and downloads the files you chose |
| Other sites whose links you open | The links panel inside the app (or your default browser, if "Open links in an external browser" is on) loads the page you opened, and also the move to Moodle from "Moodle LMS": the hand-off address is issued by the Univer site. Univer cookies are not passed there |
| Servers the site loads resources from | Univer pages may include images, videos or scripts from other sites; the site decides this, as in any browser |
| `github.com`, `*.githubusercontent.com` | The update check at every start (while the app stays open, at most once every 6 hours) and the installer download after you press Install. The check cannot be turned off. GitHub sees your IP address and the app's name; your login, password and portal data are not sent there. GitHub's rules: [GitHub Privacy Statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement) |
| `download.microsoft.com`, `download.visualstudio.microsoft.com` | Only the installer, and only if the computer lacks .NET Framework or Visual C++ and you agreed to download them |

The built-in Chromium starts with component updates and logging turned off. A downloaded update installer stays in `%LOCALAPPDATA%\Univer.Desktop\updates` and is deleted at the next start; the last check's result is kept in the settings.

## What is stored on the computer

Everything is stored locally only, in your Windows account profile:

| What | Where | Contents |
| --- | --- | --- |
| Sign-in data | `%LOCALAPPDATA%\Univer.Desktop\credentials.dpapi` | Login and password encrypted with Windows DPAPI: only your Windows account on this computer can decrypt them |
| Sign-in data with a PIN | `%LOCALAPPDATA%\Univer.Desktop\lock.dpapi`, `account.dpapi`, `lock.pin` | `lock.dpapi` holds the PIN error counter under DPAPI, and the login and password encrypted with a key from the PIN and DPAPI on top. `account.dpapi` holds only the login, `lock.pin` nothing. The `credentials.dpapi` file is then deleted |
| Settings | `%LOCALAPPDATA%\Univer.Desktop\preferences.json` | Language, theme, view, zoom, window size, tray, pinned links (up to 24), the "do not show the Legacy warning" mark, the automatic sign-in pause, the start page, the Downloads sort order. No password |
| Start with Windows | Windows registry, `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Univer.Desktop` | The path to this copy's `Univer.Desktop.exe` and the `--tray` switch. Written only when you turn the option on in Settings; turning it off removes it, and so does uninstalling the installed app |
| Downloads list | `%LOCALAPPDATA%\Univer.Desktop\downloads.json` | Names, paths, sizes and dates of downloaded files and the course a file came from; a hash is stored instead of the file's address. No cookies |
| Notifications | `%LOCALAPPDATA%\Univer.Desktop\notices.json` | Menu item titles, questionnaire names with deadlines, the site's message texts and "read" marks. No passwords or form values |
| Copy of the week's schedule | `%LOCALAPPDATA%\Univer.Desktop\schedule.json` | This week's classes (day, time, name, kind of class, room, teacher) as the app read them on the schedule page or the Bachelor (Бакалавр) page, the date of that reading and a short hash of the login. Used for class reminders and for the schedule shown without a network. Removed on sign-out, when the saved sign-in is removed and when the account changes |
| Exam schedule | `%LOCALAPPDATA%\Univer.Desktop\exams.json` | Coming exams and midterms (subject, time, room) from the exam and midterm schedule pages, which reminders were already shown, and a short hash of the login. Used for reminders. Removed on logout, when the saved sign-in is removed and on an account change |
| Latest journal totals | `%LOCALAPPDATA%\Univer.Desktop\last-grades.json` | Subjects and their scores (midterms, exam, total) the app saw on the Bachelor page, the time read and a short hash of the login. Shown on the screen without a connection. Removed on logout, when the saved sign-in is removed and on an account change |
| Last grade check | `%LOCALAPPDATA%\Univer.Desktop\grades.json` | The Current attestation table as of the last check: courses, column headings and their values (credits, midterms, exam, total, GPA, letter grade), the time of the check and a short hash of the login. The next check is compared with it to tell you what changed. Removed on sign-out, when the saved sign-in is removed, when the account changes and when grade notifications are turned off |
| Syllabus tasks | The site's storage inside the browser profile | Your educational programme's code and name, the language division, the semester's course list, week plans from the syllabi (topics and scores), the weeks in which your group has each kind of class, a short hash of the login (so another student signing in on the same profile does not see your programme, course list and group weeks), and your folded and hidden choices. Removed with the profile on sign-out |
| Goal on the grades page | The site's storage inside the browser profile | Only the chosen level (Satisfactory, Good or Excellent), so the «points left» count stays the same on the next visit. No grades are kept here. Removed with the profile on sign-out; the None button removes it at once |
| Home page panels | The site's storage inside the browser profile | What the panels showed last time: this week's classes, your scores by course (midterms, exam, total), the list of your requests with their statuses, this week's independent work from the schedule page (course, work number, task and maximum score), plus a short hash of the login and the language. This way the panels appear at once instead of after the site answers. A record older than 14 days (for the schedule and independent work, from an earlier week) is not shown and is replaced at the next read. Removed with the profile on sign-out |
| New files in UMKD | The site's storage inside the browser profile | The numbers of the files a UMKD course page had at your last visit (up to 500 per page, up to 60 pages), that page's address and a short hash of the login. From them the app marks files that appeared since then as New. No file names or files are kept here. Removed with the profile on sign-out |
| Browser profile | `%LOCALAPPDATA%\Univer.Desktop\Browser-Modern` (Modern) or `Browser` (Legacy) | Session cookies, cache and storage of the Univer site, as in an ordinary browser. The same folder holds the links panel's own profile: cookies and cache of the other sites you opened |
| Start-up profile | `%LOCALAPPDATA%\Univer.Desktop\startup-*.jit` | Names of the program's own methods that .NET compiles at start, so the next start is faster. No page or account data |
| Downloaded files | Your Downloads folder | The files you downloaded |
| Grade summary | Your Downloads folder or the clipboard | Your subjects and scores as PDF or PNG; made only when you press Save PDF, Save PNG or Copy picture in the summary yourself |
| Schedule to a calendar or as a picture | Your Downloads folder | The classes of the week shown and its neighbour (time, subject, kind of class, room, teacher) as an .ics file or a PNG; made only when you press «To calendar» or «As picture» yourself. The app keeps no copy of it for this |
| File for a fault report | Your Downloads folder | The app and Windows versions, screen size, settings, the address of the open page without its parameters and numbers, and the last errors of this run; no login, password, cookies or portal data, and the Windows user name, computer name, e-mail addresses, long numbers and tokens are replaced with markers. Made only when you press Save in Settings → App yourself, and never sent anywhere. The events themselves are kept only in memory while the app runs |

There is no personal data in the program folder (installed or portable). The installer puts only `installation-id.txt` there, with the variant name (for example `Univer.Desktop.modern.x64`), so that an update lands in the same folder.

## How to delete data

- **Sign out** ("Settings → Account"): deletes the encrypted record with your login and password and the site's cookies and storage, and replaces the browser profile; the notification list is cleared too. The sign-out link on the site itself does the same but keeps the saved password and pauses automatic sign-in.
- **Remove from list** in "Downloads": removes the entry; the file itself stays in the Downloads folder.
- **Delete everything**: uninstall the program through "Settings → Apps", then delete the `%LOCALAPPDATA%\Univer.Desktop` folder. Uninstalling does not touch this folder by itself, so that updates and reinstalls keep your sign-in.

Files are deleted the usual Windows way: there is no guaranteed erasure from the disk (especially SSDs) or from Windows backups. Signing out in the app does not end sessions on other devices.

## Browser extension

The Simple Univer extension for Chrome, Edge and Firefox has its own [extension privacy policy](https://sssox.github.io/SimpleUniver/privacy/). In short: it works only on `https://univer.kaznu.kz` pages, sends nothing to the developer or third parties, and does not read the Univer password. If the site itself shows a login and password for the university's printing or Wi-Fi, the extension puts them in a separate panel with a «Copy» button; it neither stores nor sends these values. In the browser it keeps the settings from its popup, the language, pinned sections and, while the browser is open, marks for the read notice and agreement already sent. The site's own storage keeps a cache of the study plan, programme and syllabus topics with a short hash of the login. Removing the extension erases its settings; the cache goes with univer.kaznu.kz site data in the browser settings.

## Your data on the site

Grades, schedule, questionnaires and other portal data are kept by the university on its website; the app shows them and keeps only the copies listed in the table above (the week's classes, scores, requests, syllabus plans). Al-Farabi KazNU is responsible for processing this data under its own rules.

## Changes

Changes to this policy are visible in the history of this file in the repository. Questions can be asked in the repository's issues; report vulnerabilities privately, as described in [SECURITY.en.md](SECURITY.en.md).
