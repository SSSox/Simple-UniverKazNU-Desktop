#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) ('build\release-command-qa-' + [Guid]::NewGuid().ToString('N'))
$scripts = Join-Path $root 'scripts'
New-Item -ItemType Directory -Path $scripts | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build-release.ps1') -Destination $scripts
# Exercise orchestration and failure propagation without installing or running a fixture executable.
@'
param($BrowserFlavor,$Platform,$OutputRoot,$ArtifactRoot)
if ($env:UNIVER_RELEASE_QA_FAIL -eq 'build') { throw 'Synthetic build failure' }
New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload=Join-Path $OutputRoot 'payload'; New-Item -ItemType Directory -Path $payload -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'fixture.txt'), 'Synthetic payload')
$flavors=if($BrowserFlavor -eq 'all'){@('modern','legacy')}else{@($BrowserFlavor.ToLowerInvariant())}
$arches=if($Platform -eq 'all'){@('x64','x86')}else{@($Platform.ToLowerInvariant())}
$manifest=@()
foreach($flavor in $flavors){foreach($arch in $arches){
 $zip=Join-Path $ArtifactRoot "Univer-Desktop-0.7.2-$flavor-$arch.zip"
 [IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip)
 $manifest += [pscustomobject]@{Flavor=$flavor;Architecture=$arch;Zip=[IO.Path]::GetFileName($zip);SHA256=(Get-FileHash $zip).Hash}
}}
ConvertTo-Json -InputObject @($manifest) | Set-Content (Join-Path $ArtifactRoot 'Univer-Desktop-0.7.2-manifest.json')
'@ | Set-Content (Join-Path $scripts 'build.ps1')
@'
param($BrowserFlavor,$Platform,$ArtifactRoot)
if ($env:UNIVER_RELEASE_QA_FAIL -eq 'installer') { throw 'Synthetic installer failure' }
[IO.File]::WriteAllText((Join-Path $ArtifactRoot 'installer-called.txt'), "$BrowserFlavor $Platform")
'@ | Set-Content (Join-Path $scripts 'build-installer.ps1')
@'
param($BrowserFlavor,$Platform,$OutputRoot)
if ($env:UNIVER_RELEASE_QA_FAIL -eq 'smoke') { throw 'Synthetic smoke failure' }
$dir=Join-Path $OutputRoot "$BrowserFlavor\$Platform"
if (!(Test-Path (Join-Path $dir 'fixture.txt'))) { throw 'ZIP was not fully unpacked before smoke.' }
[IO.File]::WriteAllText((Join-Path $dir 'self-test.ok'), 'PASS synthetic smoke')
'@ | Set-Content (Join-Path $scripts 'smoke.ps1')
$runner = Join-Path $scripts 'build-release.ps1'
$previous = $env:UNIVER_RELEASE_QA_FAIL
try {
    Push-Location $env:TEMP
    & $runner -Package Portable -OutputDirectory (Join-Path $root 'portable') | Out-Null
    if (Test-Path (Join-Path $root 'portable\installer-called.txt')) { throw 'Portable invoked the installer.' }
    & $runner -Package Both -BrowserFlavor all -Platform all -Verify -OutputDirectory (Join-Path $root 'both') | Out-Null
    $result = Get-Content (Join-Path $root 'both\RELEASE-RESULT.json') -Raw | ConvertFrom-Json
    if ($result.NativeChecks.Count -ne 4 -or $result.ApplicationSmoke -ne 'PASS' -or !(Test-Path (Join-Path $root 'both\installer-called.txt'))) { throw 'Missing variant, unpacking verification or installer invocation.' }
    & $runner -Package Installer -Platform x86 -OutputDirectory (Join-Path $root 'installer') | Out-Null
    if (!(Test-Path (Join-Path $root 'installer\installer-called.txt'))) { throw 'Installer mode skipped setup.' }
    $before=(Get-FileHash (Join-Path $root 'both\RELEASE-RESULT.json')).Hash
    $failure=$null
    try { & $runner -OutputDirectory (Join-Path $root 'both') | Out-Null } catch { $failure=$_.Exception.Message }
    if ($failure -notlike '*already exists*' -or (Get-FileHash (Join-Path $root 'both\RELEASE-RESULT.json')).Hash -ne $before) { throw 'Existing release was modified.' }
    foreach ($stage in @('build','smoke','installer')) {
        $env:UNIVER_RELEASE_QA_FAIL=$stage; $dist=Join-Path $root "failed-$stage"; $failure=$null
        try { & $runner -Verify -OutputDirectory $dist | Out-Null } catch { $failure=$_.Exception.Message }
        if ($failure -notlike "*Synthetic $stage failure*" -or (Test-Path (Join-Path $dist 'RELEASE-RESULT.json'))) { throw "Failed $stage produced a success result." }
    }
} finally { Pop-Location; $env:UNIVER_RELEASE_QA_FAIL=$previous }
Write-Output "PASS release modes, four variants, fully unpacked smoke, existing output guard, failure propagation, launch from another working directory: $root"
