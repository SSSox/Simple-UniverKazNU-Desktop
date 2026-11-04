# Upgrade from an earlier installed Modern x64 executable to the current Modern x64 setup.
# -OldExe: an earlier Univer.Desktop.exe (Modern x64). -ArtifactRoot: the release folder with the current setup and portable folder.
param([Parameter(Mandatory = $true)][string]$OldExe, [Parameter(Mandatory = $true)][string]$ArtifactRoot)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\Univer.Desktop\Univer.Desktop.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$artifacts = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactRoot)
$oldExe = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OldExe)
$currentSetup = Join-Path $artifacts "Univer-Desktop-$version-modern-x64-setup.exe"
$currentExe = Join-Path $artifacts "Univer-Desktop-$version-modern-x64\Univer.Desktop.exe"
foreach ($required in @($oldExe, $currentSetup, $currentExe)) { if (!(Test-Path -LiteralPath $required)) { throw "Missing upgrade input: $required" } }
$oldVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($oldExe).FileVersion
if ($oldVersion -ge [Version]"$version.0") { throw "OldExe must be older than $version (found $oldVersion)." }
$oldDisplay = '{0}.{1}.{2}' -f $oldVersion.Major, $oldVersion.Minor, $oldVersion.Build
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Univer.Desktop.modern.x64_is1'
if (Test-Path -LiteralPath $registration) { throw 'Existing Modern x64 installation will not be changed by QA.' }
if (Get-Process -Name 'Univer.Desktop' -ErrorAction SilentlyContinue) { throw 'Close Univer before upgrade QA.' }
$qaRoot = Join-Path $projectRoot ('build\installer-upgrade-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $qaRoot 'old-payload'
$destination = Join-Path $qaRoot 'installed'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item -LiteralPath $oldExe -Destination $payload
$marker = Join-Path $qaRoot 'installation-id.txt'
$info = Join-Path $qaRoot 'info.txt'
[IO.File]::WriteAllText($marker, 'Univer.Desktop.modern.x64')
[IO.File]::WriteAllText($info, 'Isolated old-version upgrade fixture. Not a distribution package.')
$pins = Get-Content -LiteralPath (Join-Path $projectRoot 'installer\prerequisites.json') -Raw | ConvertFrom-Json
$net = $pins | Where-Object Name -eq 'net462'
$vc = $pins | Where-Object Name -eq 'modern-x64'
$compiler = & (Join-Path $PSScriptRoot 'prepare-installer-tools.ps1')
$arguments = @('/Qp',"/Fupgrade-from-$oldDisplay","/DAppVersion=$oldDisplay",'/DFlavor=modern','/DArch=x64',"/DPayloadRoot=$payload","/DOutputRoot=$qaRoot","/DMarkerFile=$marker","/DInfoFile=$info","/DNetUrl=$($net.Url)","/DNetSHA256=$($net.SHA256)","/DVCUrl=$($vc.Url)","/DVCSHA256=$($vc.SHA256)","/DVCVersion=$($vc.Version)",(Join-Path $projectRoot 'installer\Univer.iss'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Old-version fixture compilation failed.' }
function Run-Setup([string]$Setup,[string[]]$Arguments) {
    $process = Start-Process -FilePath $Setup -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw 'Upgrade fixture timed out.' }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Upgrade fixture failed: $($process.ExitCode)" }
}
$common = @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOICONS','/TASKS=""',('/DIR="' + $destination + '"'))
Run-Setup (Join-Path $qaRoot "upgrade-from-$oldDisplay.exe") ($common + '/LANG=kazakh')
if ((Get-ItemProperty -LiteralPath $registration).DisplayVersion -ne $oldDisplay) { throw 'Old version not registered.' }
$sentinel = Join-Path $destination 'user-file.txt'
[IO.File]::WriteAllText($sentinel, 'Synthetic user data')
Run-Setup $currentSetup $common
$installed = Get-ItemProperty -LiteralPath $registration
if ($installed.DisplayVersion -ne $version -or $installed.'Inno Setup: Language' -ne 'kazakh') { throw 'Upgrade lost version or language.' }
$expected = (Get-FileHash -LiteralPath $currentExe -Algorithm SHA256).Hash
if ((Get-FileHash (Join-Path $destination 'Univer.Desktop.exe') -Algorithm SHA256).Hash -ne $expected) { throw 'Upgrade did not install new executable.' }
if ((Get-Content -LiteralPath $sentinel -Raw) -ne 'Synthetic user data') { throw 'Upgrade changed user file.' }
Run-Setup (Join-Path $destination 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
if ((Test-Path -LiteralPath $registration) -or !(Test-Path -LiteralPath $sentinel)) { throw 'Upgrade fixture cleanup failed.' }
Write-Output "PASS upgrade from actual $oldDisplay executable to ${version}: registry, executable, Kazakh choice and user file"
Write-Output "Evidence: $qaRoot"
