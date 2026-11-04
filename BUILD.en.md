[Русский](BUILD.md) · [Қазақша](BUILD.kz.md) · **English**

# Build, checks and release

Everything needed to build the app from these sources: portable archives, installers and checks. Users of the ready-made app need none of this.

## Requirements

- Windows 10 or 11.
- [.NET SDK](https://dotnet.microsoft.com/download) (tested with 10.0.400). The app targets .NET Framework 4.5.2 (Legacy) and 4.6.2 (Modern), the tests 4.8; the required reference assemblies come from NuGet.
- PowerShell 7 (`pwsh`); tested with 7.6.
- Node.js and npm only for the page tests (`tests/dom`).
- Git only for `test-release-packaging.ps1`.
- Internet on the first build: NuGet packages and the installer compiler.

NuGet and npm packages are pinned by lock files (`packages.lock.json`, `packages.modern.lock.json`, `package-lock.json`); restore runs in `--locked-mode`, so do not change versions without a reason.

## One command

From the repository root in PowerShell 7:

```powershell
.\scripts\build-release.ps1 -Package Portable                 # portable Modern x64
.\scripts\build-release.ps1 -Package Installer                # Modern x64 installer (and its portable base)
.\scripts\build-release.ps1 -Package Both -BrowserFlavor all -Platform all -Verify
```

Each run makes a fresh build in `build\release-<date>-<id>` and puts the result into a new `artifacts\release-<date>-<id>` folder (or into `-OutputDirectory`). `-Verify` fully unpacks every ZIP and runs the self-test from it before the installer is made. `-BrowserFlavor` takes `Modern`, `Legacy` or `all`, `-Platform` takes `x64`, `x86` or `all`. The release stops at the first error.

## Step by step

```powershell
$build = '.\build\local'
$dist = '.\artifacts\local-' + (Get-Date -Format 'yyyyMMdd-HHmmss')

# 1. App files for all four variants, without archives
.\scripts\build.ps1 -BuildOnly -OutputRoot $build
# 2. Portable: ZIP, .sha256 and manifest
.\scripts\build.ps1 -SkipBuild -OutputRoot $build -ArtifactRoot $dist
# 3. Installers from these ZIPs
.\scripts\build-installer.ps1 -ArtifactRoot $dist
```

For a single variant, pass the same `-BrowserFlavor` and `-Platform` to all three commands (for example `-BrowserFlavor Modern -Platform x64`). The scripts never overwrite existing ZIPs and installers: to package the same version again, choose a new `$dist` folder.

Legacy builds with CefSharp 109, and NuGet shows warning `NU1903` about the known Chromium 109 vulnerability. It is not suppressed: it is true.

## What you get

| File | What it is |
| --- | --- |
| `$build\<modern\|legacy>\<x64\|x86>\Univer.Desktop.exe` | The built app with the DLLs next to it, Chromium resources and `Assets` |
| `Univer-Desktop-<version>-<variant>-<arch>.zip` | Portable archive: the app, `LICENSE`, `README`, `SECURITY`, `PRIVACY`, `BUILD` and `THIRD-PARTY` in three languages (`.md`, `.kz.md`, `.en.md`), `licenses/`, `BUILD-INFO.txt` |
| `Univer-Desktop-<version>-<variant>-<arch>-setup.exe` | Installer with the same files |
| `*.sha256` | SHA-256 of every ZIP and installer |
| `Univer-Desktop-<version>-manifest.json`, `-installers.json` | List of release files with sizes and SHA-256 |

Packaging checks that the `Assets` in the build match the sources, that no self-test files, logs, `.pdb`, sign-in data or browser profile got into the package, and that it contains only the Chromium languages `ru`, `en-GB`, `en-US` and no WebGPU compiler. If a check fails, rebuild into a new `-OutputRoot` folder.

The installer is built with Inno Setup 7.1.0: `scripts/prepare-installer-tools.ps1` downloads it into `tools\` from the official release, checks its SHA-256 and publisher signature and unpacks it without installing it into the system. Microsoft components are not bundled into the installer: their addresses, versions and SHA-256 are pinned in `installer/prerequisites.json`.

## Checks

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

`smoke.ps1` starts the built app with `--self-test` in a separate temporary profile: windows, sign-in on synthetic pages, downloads, the file viewer, themes, notifications, sign-out. It touches neither a real account nor your Univer profile. `-Live` adds a read of the public `univer.kaznu.kz` sign-in page without submitting the form or any sign-in data. `-Only <step>` runs one step (`settings`, `shell`, `document`, `presentation`, `form-download`, `layout-load`, `downloads`, `office`, `zoom`, `theme`, `notices`, `live`, `logout`). Repeat smoke for every variant you release.

The installer checks install and remove the app in a separate folder and never touch an already installed Univer:

```powershell
.\scripts\smoke-installer.ps1 -ArtifactRoot $dist                     # install, repair, uninstall
.\scripts\test-installer-upgrade.ps1 -OldExe <Univer.Desktop.exe of the previous version> -ArtifactRoot $dist
.\scripts\test-installer-guards.ps1                                   # foreign and network folders, junctions, old over new, running app
.\scripts\test-installer-download.ps1                                 # Inno Setup rejects a file with a wrong SHA-256 (needs network)
```

## Scripts

| Script | What it does |
| --- | --- |
| `build-release.ps1` | Build, packaging, checks and installer in one command |
| `build.ps1` | Builds the variants (`-BuildOnly`), packages portable (`-SkipBuild`) or both |
| `build-installer.ps1` | Installers from the portable archives and manifest |
| `prepare-installer-tools.ps1` | Downloads and checks Inno Setup 7.1.0 into `tools\` |
| `smoke.ps1` | Self-test of a built variant |
| `smoke-installer.ps1` | Install, repair and uninstall of a finished installer in an isolated folder |
| `test-installer-guards.ps1`, `test-installer-upgrade.ps1`, `test-installer-download.ps1` | Installer guards, upgrade from a previous version, SHA-256 check of downloads |
| `test-localization.ps1` | Every interface and installer string exists in Russian, Kazakh and English |
| `test-release-packaging.ps1`, `test-build-release.ps1` | Packaging, manifests, no-overwrite rules and `.gitignore` rules on small synthetic files |
| `update-pdfjs.ps1` | Updates `Assets/pdfjs` from npm with a check of the pinned SHA-512 |
| `apply-signed-files.ps1` | Checks the signature of files from SignPath, replaces the unsigned ones and updates the installer SHA-256 and manifest |

## New version

1. Raise the version in `src/Univer.Desktop/Univer.Desktop.csproj` (`<Version>`), `src/Univer.Desktop/app.manifest` (`assemblyIdentity`, four parts) and `package.json`, then run `npm.cmd install --package-lock-only --ignore-scripts`. The `VersionTests` test checks that the versions match.
2. Build all four variants and run the checks above for each.
3. Push the tag `vX.Y.Z` (see below): GitHub builds and attaches the ZIPs, installers, their `.sha256` and both manifests. In the release description list the changes and which systems were tested. Ready-made files are not added to the repository history.

## Release on GitHub

Public releases are built by `.github/workflows/release.yml` on a clean GitHub machine. The tag `vX.Y.Z` must match the project version: the build runs the tests, builds the four variants, checks that they start, packages them and publishes the release. A manual run does the same but only keeps the files for 7 days.

Signing turns on when the repository has the `SIGNPATH_ORGANIZATION_ID` and `SIGNPATH_POLICY` variables and the `SIGNPATH_API_TOKEN` secret. The build then waits twice for manual approval in SignPath: first for `Univer.Desktop.exe`, then for the installers, which already contain the signed application. The signing settings are in `.signpath/artifact-configurations/`.

## Build for the owner's review

The crawler of the real portal (`UI/Qa/MainForm.Review.cs` and the files next to it) is not part of the normal build. It is built only explicitly and only for the owner's local review, and is never shared:

```powershell
dotnet build .\src\Univer.Desktop\Univer.Desktop.csproj -c Release -p:Platform=x64 -p:BrowserFlavor=Modern -p:QaReview=true
```
