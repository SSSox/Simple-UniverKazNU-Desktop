param(
    [ValidateSet('x64','x86','all')][string]$Platform = 'all',
    [ValidateSet('modern','legacy','all')][string]$BrowserFlavor = 'all',
    [switch]$RunApplicationSmoke, [string]$ArtifactRoot
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\Univer.Desktop\Univer.Desktop.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (!$ArtifactRoot) { throw 'Specify -ArtifactRoot for the installer release being tested.' }
$artifacts = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactRoot)
$qaRoot = Join-Path $projectRoot ('build\installer-qa-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qaRoot | Out-Null
$results = @()
$flavors = if ($BrowserFlavor -eq 'all') { @('modern','legacy') } else { @($BrowserFlavor) }
$platforms = if ($Platform -eq 'all') { @('x64','x86') } else { @($Platform) }

function Invoke-Setup([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(120000)) { throw "Setup timed out; inspect logs in $qaRoot. Its process is $($process.Id)." }
    $process.Refresh()
    return $process.ExitCode
}

function Get-AccountFingerprint {
    # Hash only, never decrypt/read account values or print personal data.
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Univer.Desktop'
    $fingerprints = foreach ($name in @('credentials.dpapi','preferences.json','downloads.json')) {
        $file = Join-Path $root $name
        if (Test-Path -LiteralPath $file) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } else { 'absent' }
    }
    return ($fingerprints -join ':')
}

if (Get-Process -Name 'Univer.Desktop' -ErrorAction SilentlyContinue) { throw 'Close Univer before running installer QA; it will not be closed automatically.' }
$accountBefore = Get-AccountFingerprint
foreach ($flavor in $flavors) {
    foreach ($arch in $platforms) {
        $id = "Univer.Desktop.$flavor.$arch"
        $registration = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${id}_is1"
        if (Test-Path -LiteralPath $registration) { throw "Already installed: $id. QA will not change an existing registration." }
        $setup = Join-Path $artifacts "Univer-Desktop-$version-$flavor-$arch-setup.exe"
        $expectedHash = (Get-Content -LiteralPath "$setup.sha256" -Raw).Split(' ')[0]
        if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Installer checksum mismatch.' }
        $destination = [IO.Path]::GetFullPath((Join-Path $qaRoot "$flavor\$arch"))
        if (!$destination.StartsWith($qaRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe QA path.' }
        $group = 'Univer Installer QA ' + [IO.Path]::GetFileName($qaRoot) + " $flavor $arch"
        $common = @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOCLOSEAPPLICATIONS','/NOFORCECLOSEAPPLICATIONS','/TASKS=""','/LANG=russian',('/DIR="' + $destination + '"'),('/GROUP="' + $group + '"'))
        $installCode = Invoke-Setup $setup ($common + ('/LOG="' + (Join-Path $qaRoot "$flavor-$arch-install.log") + '"'))
        if ($installCode -ne 0) { throw "Install failed: $flavor $arch ($installCode)" }
        $installed = Get-ItemProperty -LiteralPath $registration
        if ($installed.DisplayVersion -ne $version -or $installed.InstallLocation.TrimEnd('\') -ne $destination) { throw 'Invalid installation registration.' }
        $portable = Join-Path $artifacts "Univer-Desktop-$version-$flavor-$arch"
        $matched = 0
        foreach ($source in Get-ChildItem -LiteralPath $portable -File -Recurse) {
            $relative = $source.FullName.Substring($portable.Length + 1)
            $target = Join-Path $destination $relative
            if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw "Installed payload mismatch: $relative" }
            $matched++
        }
        $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$group\Univer.lnk"
        if (!(Test-Path -LiteralPath $shortcut)) { throw 'Start menu shortcut missing.' }
        $shell = New-Object -ComObject WScript.Shell
        if ($shell.CreateShortcut($shortcut).TargetPath -ne (Join-Path $destination 'Univer.Desktop.exe')) { throw 'Invalid shortcut target.' }
        $sentinel = Join-Path $destination 'keep-user-file.txt'
        [IO.File]::WriteAllText($sentinel, 'QA synthetic user file; must survive repair and uninstall.')
        $repairCodes = @()
        foreach ($language in @('english','kazakh')) {
            $repairArguments = @($common | Where-Object { $_ -notlike '/LANG=*' }) + "/LANG=$language"
            $repairCode = Invoke-Setup $setup ($repairArguments + ('/LOG="' + (Join-Path $qaRoot "$flavor-$arch-repair-$language.log") + '"'))
            if ($repairCode -ne 0 -or !(Test-Path -LiteralPath $sentinel)) { throw 'Repair failed or removed a user file.' }
            if ((Get-ItemProperty -LiteralPath $registration).'Inno Setup: Language' -ne $language) { throw "Installer language not persisted: $language" }
            $repairCodes += $repairCode
        }
        $checks = $null
        if ($RunApplicationSmoke) {
            $smoke = & (Join-Path $PSScriptRoot 'smoke.ps1') -BrowserFlavor $flavor -Platform $arch -OutputRoot $qaRoot
            $checks = $smoke -join [Environment]::NewLine
        }
        $uninstaller = Join-Path $destination 'unins000.exe'
        $uninstallCode = Invoke-Setup $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $qaRoot "$flavor-$arch-uninstall.log") + '"'))
        if ($uninstallCode -ne 0 -or (Test-Path -LiteralPath $registration) -or (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath (Join-Path $destination 'Univer.Desktop.exe'))) { throw 'Uninstall did not remove owned files, shortcut, or registration.' }
        if (!(Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed a user file.' }
        if ((Get-AccountFingerprint) -ne $accountBefore) { throw 'Account storage changed during installer QA.' }
        $results += [pscustomobject]@{Flavor=$flavor;Architecture=$arch;Install=$installCode;Repair=$repairCodes;Languages=@('russian','english','kazakh');Uninstall=$uninstallCode;PayloadFilesMatched=$matched;ShortcutVerified=$true;UserFilePreserved=$true;AccountUnchanged=$true;ApplicationSmoke=$checks}
        Write-Output "$flavor ${arch}: installation, repair and uninstall passed; $matched files matched."
    }
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $qaRoot 'results.json') -Encoding utf8
Write-Output "Results: $qaRoot\results.json"
