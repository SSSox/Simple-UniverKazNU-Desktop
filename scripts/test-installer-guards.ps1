# -PayloadExe: any built Univer.Desktop.exe (default: the Modern x64 Release build); it is only fixture payload.
param([string]$PayloadExe)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$qaRoot = Join-Path $projectRoot ('build\installer-guards-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $qaRoot 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
$compiler = & (Join-Path $PSScriptRoot 'prepare-installer-tools.ps1')
$pins = Get-Content -LiteralPath (Join-Path $projectRoot 'installer\prerequisites.json') -Raw | ConvertFrom-Json
$net = $pins | Where-Object Name -eq 'net462'
$vc = $pins | Where-Object Name -eq 'modern-x64'
if (!$PayloadExe) { $PayloadExe = Join-Path $projectRoot 'src\Univer.Desktop\bin\Modern\x64\Release\net462\Univer.Desktop.exe' }
if (!(Test-Path -LiteralPath $PayloadExe)) { throw "Fixture payload missing: $PayloadExe. Build Modern x64 or pass -PayloadExe." }
$payloadVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($PayloadExe).FileVersion
$fixtureVersion = '{0}.{1}.{2}' -f $payloadVersion.Major, $payloadVersion.Minor, $payloadVersion.Build
Copy-Item -LiteralPath $PayloadExe -Destination $payload
$id = 'Univer.Installer.Guards.' + [IO.Path]::GetFileName($qaRoot)
$marker = Join-Path $qaRoot 'installation-id.txt'
$info = Join-Path $qaRoot 'info.txt'
[IO.File]::WriteAllText($marker, $id)
[IO.File]::WriteAllText($info, 'Isolated installer regression fixture; not for distribution.')
$baseArgs = @('/Qp', "/DInstallId=$id", '/DFlavor=modern', '/DArch=x64', "/DPayloadRoot=$payload", "/DOutputRoot=$qaRoot", "/DMarkerFile=$marker", "/DInfoFile=$info", "/DNetUrl=$($net.Url)", "/DNetSHA256=$($net.SHA256)", "/DVCUrl=$($vc.Url)", "/DVCSHA256=$($vc.SHA256)")
$script = Join-Path $projectRoot 'installer\Univer.iss'
function Build-Fixture([string]$Name, [string]$Version, [string]$VCVersion, [string]$Source = $script) {
    & $compiler @baseArgs "/F$Name" "/DAppVersion=$Version" "/DVCVersion=$VCVersion" $Source
    if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
    return (Join-Path $qaRoot "$Name.exe")
}
function Expect-Rejection([string]$Setup, [string]$Destination, [string]$Name, [string]$ErrorText) {
    $log = Join-Path $qaRoot "$Name.log"
    $process = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOICONS','/TASKS=""','/LANG=english',('/DIR="' + $Destination + '"'),('/LOG="' + $log + '"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) { throw "Guard fixture timeout: $Name" }
    $process.Refresh()
    if ($process.ExitCode -eq 0) { throw "Guard did not reject: $Name" }
    $logText = Get-Content -LiteralPath $log -Raw
    if ($logText -notlike "*$ErrorText*") { throw "Wrong rejection: $Name" }
    Write-Output "PASS $Name (exit $($process.ExitCode))"
}
$normal = Build-Fixture 'guards' $fixtureVersion $vc.Version
$empty = Join-Path $qaRoot 'empty'
$foreign = Join-Path $qaRoot 'foreign'
New-Item -ItemType Directory -Path $foreign | Out-Null
[IO.File]::WriteAllText((Join-Path $foreign 'user.txt'), 'Synthetic user data')
Expect-Rejection $normal $foreign 'nonempty-folder' 'Choose a separate empty folder'
$account = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Univer.Desktop'
Expect-Rejection $normal $account 'account-folder' 'Choose a separate empty folder'
Expect-Rejection $normal '\\localhost\UniverInstallerQA' 'network-folder' 'Choose a separate empty folder'
$junction = Join-Path $qaRoot 'junction'
New-Item -ItemType Junction -Path $junction -Target $foreign | Out-Null
Expect-Rejection $normal (Join-Path $junction 'new-app') 'junction-ancestor' 'Choose a separate empty folder'
$other = Join-Path $qaRoot 'other-edition'
New-Item -ItemType Directory -Path $other | Out-Null
[IO.File]::WriteAllText((Join-Path $other 'installation-id.txt'), 'Univer.Desktop.legacy.x86')
Expect-Rejection $normal $other 'other-edition' 'Choose a separate empty folder'
$older = Build-Fixture 'downgrade' '0.0.1' $vc.Version
$installed = Join-Path $qaRoot 'newer-app'
New-Item -ItemType Directory -Path $installed | Out-Null
Copy-Item -LiteralPath (Join-Path $payload 'Univer.Desktop.exe') -Destination $installed
Copy-Item -LiteralPath $marker -Destination $installed
Expect-Rejection $older $installed 'downgrade' 'A newer version of Univer'
$missing = Build-Fixture 'missing-components' $fixtureVersion '99.0.0.0'
Expect-Rejection $missing $empty 'missing-components-silent' 'Install the required Microsoft components first'
if (Test-Path -LiteralPath $empty) { throw 'Missing prerequisites wrote application files.' }
$restartSource = Join-Path $qaRoot 'restart.iss'
$sourceText = Get-Content -LiteralPath $script -Raw
$restartFixture = $sourceText.Replace('Missing := MissingComponents;', 'RestartPending := True; Missing := MissingComponents;').Replace('..\src\Univer.Desktop\app.ico', (Join-Path $projectRoot 'src\Univer.Desktop\app.ico')).Replace('MessagesFile: "Kazakh.isl"', ('MessagesFile: "' + (Join-Path $projectRoot 'installer\Kazakh.isl') + '"'))
[IO.File]::WriteAllText($restartSource, $restartFixture, [Text.UTF8Encoding]::new($true))
$restart = Build-Fixture 'restart-pending' $fixtureVersion $vc.Version $restartSource
Expect-Rejection $restart $empty 'restart-pending' 'Restart Windows to complete component installation'
if (Test-Path -LiteralPath $empty) { throw 'Pending restart wrote application files.' }
$created = $false
$mutex = [Threading.Mutex]::new($true, ('Local\Univer.Desktop.' + [Environment]::UserName), [ref]$created)
if (!$created) { $mutex.Dispose(); throw 'A real app mutex exists; busy-app test will not interfere.' }
try { Expect-Rejection $normal $empty 'running-app-mutex' 'Setup has detected that Univer' }
finally { $mutex.ReleaseMutex(); $mutex.Dispose() }

# Required mutation experiment: prove the foreign-directory assertion detects a removed guard.
$mutatedScript = Join-Path $qaRoot 'mutated.iss'
$sourceText = Get-Content -LiteralPath $script -Raw
$mutation = $sourceText.Replace('if not SafeInstallDirectory then begin', 'if False then begin')
if ($mutation -eq $sourceText) { throw 'Mutation target not found.' }
# Relative icon paths are resolved against the temporary script, so use the observed absolute source icon.
$mutation = $mutation.Replace('..\src\Univer.Desktop\app.ico', (Join-Path $projectRoot 'src\Univer.Desktop\app.ico')).Replace('MessagesFile: "Kazakh.isl"', ('MessagesFile: "' + (Join-Path $projectRoot 'installer\Kazakh.isl') + '"'))
[IO.File]::WriteAllText($mutatedScript, $mutation, [Text.UTF8Encoding]::new($true))
$mutant = Build-Fixture 'mutant' $fixtureVersion $vc.Version $mutatedScript
$mutationCaught = $false
try { Expect-Rejection $mutant $foreign 'mutation-foreign-folder' 'Choose a separate empty folder' }
catch { if ($_.Exception.Message -notlike '*Guard did not reject*') { throw }; $mutationCaught = $true }
if (!$mutationCaught) { throw 'The directory guard regression did not catch the mutation.' }
$uninstaller = Join-Path $foreign 'unins000.exe'
$cleanup = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
if (!$cleanup.WaitForExit(30000)) { throw 'Mutation fixture uninstall timed out.' }
$cleanup.Refresh()
if ($cleanup.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $foreign 'user.txt'))) { throw 'Mutation cleanup failed.' }
Write-Output 'PASS mutation detected and isolated fixture uninstalled'
Write-Output "Guard evidence: $qaRoot"
