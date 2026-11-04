param([Parameter(Mandatory=$true)][string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\Univer.Desktop\Univer.Desktop.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$qaRoot = Join-Path $projectRoot ('build\release-packaging-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $qaRoot 'project'
$sourceOutput = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path "$fixture\scripts","$fixture\src\Univer.Desktop\Assets","$fixture\docs\android-source","$fixture\licenses","$fixture\installer" -Force | Out-Null
foreach ($script in @('build.ps1','build-installer.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination "$fixture\scripts" }
Copy-Item -LiteralPath (Join-Path $projectRoot '.gitignore') -Destination $fixture
[IO.File]::WriteAllText("$fixture\src\Univer.Desktop\Univer.Desktop.csproj", "<Project><PropertyGroup><Version>$version</Version></PropertyGroup></Project>")
[IO.File]::WriteAllText("$fixture\installer\prerequisites.json", '[]')
[IO.File]::WriteAllText("$fixture\src\Univer.Desktop\Assets\fixture.js", 'Fixture.')
foreach ($file in @('LICENSE','README.md','README.kz.md','README.en.md','SECURITY.md','SECURITY.kz.md','SECURITY.en.md','PRIVACY.md','PRIVACY.kz.md','PRIVACY.en.md','BUILD.md','BUILD.kz.md','BUILD.en.md','THIRD-PARTY.md','THIRD-PARTY.kz.md','THIRD-PARTY.en.md','README.de.md','COMPATIBILITY.md','VALIDATION.md','ROADMAP.md','AGENTS.md','SKILLS.md','CLAUDE.md','docs\android-source\README.md','docs\BUILD.md','licenses\test.txt')) { [IO.File]::WriteAllText((Join-Path $fixture $file), 'Offline packaging fixture.') }

# Small offline payloads exercise packaging only; the synthetic libcef is never executed.
foreach ($arch in @('x64','x86')) {
    $output = Join-Path $fixture "output\modern\$arch"
    New-Item -ItemType Directory -Path "$output\Assets","$output\qa","$output\qa-study" -Force | Out-Null
    foreach ($file in @('Univer.Desktop.exe','CefSharp.dll')) { Copy-Item -LiteralPath (Join-Path $sourceOutput "modern\$arch\$file") -Destination $output }
    $native = New-Object byte[] 256
    [BitConverter]::GetBytes([int]0x80).CopyTo($native, 0x3c)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($native, 0x80)
    $machine = if ($arch -eq 'x64') { [uint16]0x8664 } else { [uint16]0x014c }
    [BitConverter]::GetBytes($machine).CopyTo($native, 0x84)
    [IO.File]::WriteAllBytes("$output\libcef.dll", $native)
    foreach ($file in @('Assets\fixture.js','self-test.ok','qa-keys.trace','fixture.log','fixture.pdb','qa\fixture.txt','qa-study\fixture.png')) { [IO.File]::WriteAllText((Join-Path $output $file), 'Fixture.') }
    # The trimmed Chromium runtime ships only the locales the app selects.
    New-Item -ItemType Directory -Path "$output\locales" -Force | Out-Null
    foreach ($locale in @('ru','ru_FEMININE','en-GB','en-US')) { [IO.File]::WriteAllText((Join-Path $output "locales\$locale.pak"), 'Fixture.') }
}
function Assert-Failure([scriptblock]$Action, [string]$Expected) {
    $failure = $null
    try { & $Action | Out-Null } catch { $failure = $_.Exception.Message }
    if (!$failure -or $failure -notlike "*$Expected*") { throw "Expected failure '$Expected'; got '$failure'." }
}
$builder = "$fixture\scripts\build.ps1"
$installer = "$fixture\scripts\build-installer.ps1"
$build = "$fixture\output"
$dist = "$fixture\artifacts"
Assert-Failure { & $builder -BuildOnly -SkipBuild -OutputRoot $build } 'Use either'
Assert-Failure { & $installer } 'Specify -ArtifactRoot'
Assert-Failure { & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot "$build\modern\x64\nested" } 'ArtifactRoot must be outside'
& $builder -SkipBuild -BrowserFlavor modern -Platform X64 -OutputRoot $build -ArtifactRoot $dist | Out-Null
$asset = "$build\modern\x64\Assets\fixture.js"
$assetTime = (Get-Item -LiteralPath $asset).LastWriteTimeUtc
[IO.File]::WriteAllText($asset, 'Stale interface, even with a future modification timestamp.')
(Get-Item -LiteralPath $asset).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(1)
Assert-Failure { & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot "$fixture\stale-assets" } 'Assets differ from current source'
[IO.File]::WriteAllText($asset, 'Fixture.')
(Get-Item -LiteralPath $asset).LastWriteTimeUtc = $assetTime
[IO.File]::WriteAllText("$build\modern\x64\Assets\removed.js", 'Removed from source.')
Assert-Failure { & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot "$fixture\removed-assets" } 'Assets differ from current source'
Remove-Item -LiteralPath "$build\modern\x64\Assets\removed.js"
[IO.File]::WriteAllText("$build\modern\x64\locales\de.pak", 'Unused locale.')
Assert-Failure { & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot "$fixture\extra-locale" } 'Unexpected Chromium locales'
Remove-Item -LiteralPath "$build\modern\x64\locales\de.pak"
[IO.File]::WriteAllText("$build\modern\x64\dxcompiler.dll", 'Untrimmed.')
Assert-Failure { & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot "$fixture\webgpu-compiler" } 'Untrimmed Chromium runtime'
Remove-Item -LiteralPath "$build\modern\x64\dxcompiler.dll"
Push-Location $fixture
try {
    & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot '.\output' -ArtifactRoot '.\relative-release' | Out-Null
    if (!(Test-Path -LiteralPath (Join-Path $fixture "relative-release\Univer-Desktop-$version-manifest.json"))) { throw 'Relative output paths ignored the PowerShell working directory.' }
} finally { Pop-Location }
$manifestPath = Join-Path $dist "Univer-Desktop-$version-manifest.json"
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if ($manifest.Count -ne 1 -or !(Get-Content -LiteralPath $manifestPath -Raw).TrimStart().StartsWith('[')) { throw 'A single-variant manifest must be an array.' }
$release = $manifest[0]
if ($release.Version -ne $version -or $release.Architecture -ne 'x64' -or $release.Flavor -ne 'modern' -or $release.Bytes -ne (Get-Item -LiteralPath "$dist\$($release.Zip)").Length -or $release.SHA256 -ne (Get-FileHash -LiteralPath "$dist\$($release.Zip)").Hash -or $release.ExeSHA256 -ne (Get-FileHash -LiteralPath "$build\modern\x64\Univer.Desktop.exe").Hash) { throw 'Portable manifest does not describe the actual files.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead("$dist\$($release.Zip)")
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
    if (@('Assets/fixture.js','licenses/test.txt','LICENSE','README.md','README.kz.md','README.en.md','SECURITY.md','SECURITY.kz.md','SECURITY.en.md','PRIVACY.md','PRIVACY.kz.md','PRIVACY.en.md','BUILD.md','BUILD.kz.md','BUILD.en.md','THIRD-PARTY.md','THIRD-PARTY.kz.md','THIRD-PARTY.en.md') | Where-Object { $names -notcontains $_ }) { throw 'Required files missing from portable.' }
    if (@($names | Where-Object { $_ -match '(^|/)(AGENTS|SKILLS|CLAUDE)\.md$|(^|/)qa(-study)?/|self-test\.|qa-keys\.|\.(log|pdb)$|^docs/|^(COMPATIBILITY|VALIDATION|ROADMAP)\.md$|^README\.de\.md$' }).Count) { throw 'Local instructions, notes or QA files leaked into portable.' }
} finally { $archive.Dispose() }
$release | Add-Member -NotePropertyName VerificationNote -NotePropertyValue 'Fixture metadata must survive a partial build.'
[IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject @($manifest)))
& $builder -SkipBuild -BrowserFlavor Modern -Platform x86 -OutputRoot $build -ArtifactRoot $dist | Out-Null
$manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
if ($manifest.Count -ne 2 -or $manifest[0].VerificationNote -ne $release.VerificationNote) { throw 'A partial build lost earlier variants or their metadata.' }
$before = (Get-FileHash -LiteralPath $manifestPath).Hash
Assert-Failure { & $builder -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot $dist } 'Package already exists'
if ((Get-FileHash -LiteralPath $manifestPath).Hash -ne $before) { throw 'A duplicate build modified the release manifest.' }
Assert-Failure { & $installer -ArtifactRoot "$fixture\missing" } 'Portable manifest not found'
$badDist = "$fixture\bad-manifest"
New-Item -ItemType Directory -Path $badDist | Out-Null
$badRelease = $manifest[0] | Select-Object *
$badRelease.Zip = '..\outside.zip'
[IO.File]::WriteAllText((Join-Path $badDist "Univer-Desktop-$version-manifest.json"), (ConvertTo-Json -InputObject @($badRelease)))
Assert-Failure { & $installer -BrowserFlavor modern -Platform x64 -ArtifactRoot $badDist } 'Invalid portable manifest entry'
[IO.File]::WriteAllText((Join-Path $dist "Univer-Desktop-$version-modern-x64-setup.exe"), 'Existing fixture installer.')
Assert-Failure { & $installer -BrowserFlavor modern -Platform x64 -ArtifactRoot $dist } 'Installer already exists'

# A stub compiler checks installer orchestration without installing software or downloading tools.
Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\prerequisites.json') -Destination "$fixture\installer\prerequisites.json" -Force
foreach ($file in @('Univer.iss','Kazakh.isl')) { Copy-Item -LiteralPath (Join-Path $projectRoot "installer\$file") -Destination "$fixture\installer" }
$stubCompiler = @'
$defines = @{}
foreach ($argument in $args) { if ($argument -match '^/D([^=]+)=(.*)$') { $defines[$matches[1]] = $matches[2] } }
$setup = Join-Path $defines.OutputRoot "Univer-Desktop-$($defines.AppVersion)-$($defines.Flavor)-$($defines.Arch)-setup.exe"
[IO.File]::WriteAllText($setup, 'Offline compiler stub; not an executable.')
$global:LASTEXITCODE = 0
'@
[IO.File]::WriteAllText("$fixture\scripts\stub-compiler.ps1", $stubCompiler)
[IO.File]::WriteAllText("$fixture\scripts\prepare-installer-tools.ps1", 'Write-Output (Join-Path $PSScriptRoot ''stub-compiler.ps1'')')
$installerDist = "$fixture\installer-artifacts"
New-Item -ItemType Directory -Path $installerDist | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination $installerDist
foreach ($entry in $manifest) { Copy-Item -LiteralPath (Join-Path $dist $entry.Zip) -Destination $installerDist }
& $installer -BrowserFlavor Modern -Platform X64 -ArtifactRoot $installerDist | Out-Null
$installerManifestPath = Join-Path $installerDist "Univer-Desktop-$version-installers.json"
$installerManifest = @(Get-Content -LiteralPath $installerManifestPath -Raw | ConvertFrom-Json)
if ($installerManifest.Count -ne 1 -or !(Get-Content -LiteralPath $installerManifestPath -Raw).TrimStart().StartsWith('[')) { throw 'A single-variant installer manifest must be an array.' }
$installerManifest[0] | Add-Member -NotePropertyName VerificationNote -NotePropertyValue 'Preserve installer metadata.'
[IO.File]::WriteAllText($installerManifestPath, (ConvertTo-Json -InputObject @($installerManifest)))
& $installer -BrowserFlavor modern -Platform x86 -ArtifactRoot $installerDist | Out-Null
$installerManifest = @(Get-Content -LiteralPath $installerManifestPath -Raw | ConvertFrom-Json)
if ($installerManifest.Count -ne 2 -or $installerManifest[0].VerificationNote -ne 'Preserve installer metadata.') { throw 'A partial installer build lost earlier variants or metadata.' }
foreach ($entry in $installerManifest) { if ($entry.SHA256 -ne (Get-FileHash -LiteralPath (Join-Path $installerDist $entry.Installer)).Hash) { throw 'Installer manifest checksum does not describe the generated file.' } }

[IO.File]::WriteAllText("$build\modern\x64\credentials.dpapi", 'Synthetic private-data sentinel.')
Assert-Failure { & $builder -SkipBuild -BrowserFlavor Modern -Platform x64 -OutputRoot $build -ArtifactRoot "$fixture\private-output" } 'Local account or browser data found'

# Verify real Git rules without initializing the user's project or reading account data.
& git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Git fixture initialization failed.' }
$ignored = @('AGENTS.md','docs/AGENTS.local.md','SKILLS.md','.grok/hooks/tool.json','.impeccable/config.local.json','graphify-out/graph.json','Project-threat-model.md','docs/REVIEW-0.6.md','docs/POLISH-REVIEW-20261028.md','docs/review-20261010/shot.png','notices.json','.agents/skills/example/SKILL.md','.codex/state.json','.claude/settings.json','.github/copilot-instructions.md','build/release/app.exe','artifacts/release.zip','tools/ISCC.exe','src/Univer.Desktop/bin/app.exe','src/Univer.Desktop/obj/assets.json','node_modules/package/index.js','DawnCache/data_0','GPUCache/data_0','Browser/profile/Cookies','Browser-Modern/profile/Local State','credentials.dpapi','preferences.json','downloads.json','.env.local','signing.pfx','docs/android-source/README.md','COMPATIBILITY.md','VALIDATION.md','ROADMAP.md','DESIGN.md','README.de.md','SECURITY.ru.md','docs/SECURITY.md','docs/screenshots/README.md','anti-slop/audit-001.md','docs/BUILD.md','docs/RELEASE-NOTES-0.13.0.md','docs/screenshots/main.png','build-release.cmd','scripts/.claude/change-ledger/base/build.ps1','scripts/DawnCache/data_0','tests/dom/.cursor/rules/style.mdc','.specstory/history/chat.md','src/opencode.json','capture.har','screenshot.png')
$kept = @('.gitignore','.gitattributes','.github/workflows/release.yml','.signpath/artifact-configurations/application.xml','.signpath/artifact-configurations/installers.xml','LICENSE','README.md','README.kz.md','README.en.md','SECURITY.md','SECURITY.kz.md','SECURITY.en.md','PRIVACY.md','PRIVACY.kz.md','PRIVACY.en.md','BUILD.md','BUILD.kz.md','BUILD.en.md','THIRD-PARTY.md','THIRD-PARTY.kz.md','THIRD-PARTY.en.md','src/Univer.Desktop/Univer.Desktop.csproj','src/Univer.Desktop/UI/Qa/MainForm.Qa.cs','src/Univer.Desktop/Assets/fixture.js','src/Univer.Desktop/packages.lock.json','src/Univer.Desktop/packages.modern.lock.json','package-lock.json','tests/dom/news-study.test.cjs','scripts/build.ps1','installer/Univer.iss','installer/Kazakh.isl','installer/prerequisites.json','licenses/CEF-LICENSE.txt','src/Univer.Desktop/Assets/pdfjs/LICENSE','scripts/build-release.ps1','Simple-UniverKazNU.sln','package.json','.github/workflows/build.yml','.env.example')
$actualIgnored = @(& git -C $fixture check-ignore --no-index -- @ignored @kept) | ForEach-Object { $_.Trim('"') }
if ($LASTEXITCODE -ne 0 -or @($ignored | Where-Object { $_ -notin $actualIgnored }).Count -or @($kept | Where-Object { $_ -in $actualIgnored }).Count) { throw 'Git rules exclude required files or include local files.' }

# Signed files replace the originals only after their signature is checked.
$signer = Join-Path $PSScriptRoot 'apply-signed-files.ps1'
$signedRoot = "$fixture\signed"
$signedFile = "$signedRoot\modern\x64\Univer.Desktop.exe"
New-Item -ItemType Directory -Path (Split-Path $signedFile) -Force | Out-Null
Copy-Item -LiteralPath "$build\modern\x64\Univer.Desktop.exe" -Destination $signedFile
New-Item -ItemType Directory -Path "$fixture\unsigned-result\modern\x64" -Force | Out-Null
[IO.File]::WriteAllText("$fixture\unsigned-result\modern\x64\Univer.Desktop.exe", 'Unsigned.')
Assert-Failure { & $signer -SignedRoot "$fixture\unsigned-result" -TargetRoot $build -TestCertificate } 'Not signed'
$certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Univer packaging fixture' -CertStoreLocation Cert:\CurrentUser\My
try {
    Set-AuthenticodeSignature -LiteralPath $signedFile -Certificate $certificate | Out-Null
    Assert-Failure { & $signer -SignedRoot $signedRoot -TargetRoot $build } 'Unexpected signature'
    Assert-Failure { & $signer -SignedRoot $signedRoot -TargetRoot "$fixture\no-output" -TestCertificate } 'no unsigned original'
    Push-Location -LiteralPath $fixture
    try { & $signer -SignedRoot 'signed' -TargetRoot 'output' -TestCertificate | Out-Null } finally { Pop-Location }
    if ((Get-FileHash -LiteralPath "$build\modern\x64\Univer.Desktop.exe").Hash -ne (Get-FileHash -LiteralPath $signedFile).Hash) { throw 'The signed application did not replace the original.' }
    $setupName = "Univer-Desktop-$version-modern-x64-setup.exe"
    $setupRoot = "$fixture\signed-installers"
    $setupTarget = "$fixture\installer-output"
    New-Item -ItemType Directory -Path $setupRoot,$setupTarget -Force | Out-Null
    Copy-Item -LiteralPath $signedFile -Destination "$setupRoot\$setupName"
    [IO.File]::WriteAllText("$setupTarget\$setupName", 'Unsigned.')
    $setupManifest = "$setupTarget\Univer-Desktop-$version-installers.json"
    [IO.File]::WriteAllText($setupManifest, (ConvertTo-Json -InputObject @([pscustomobject]@{ Installer=$setupName; Bytes=9; SHA256='OLD'; Signed=$false })))
    & $signer -SignedRoot $setupRoot -TargetRoot $setupTarget -TestCertificate | Out-Null
    $setupEntry = @(Get-Content -LiteralPath $setupManifest -Raw | ConvertFrom-Json)
    $setupHash = (Get-FileHash -LiteralPath "$setupTarget\$setupName" -Algorithm SHA256).Hash
    if ($setupEntry.Count -ne 1 -or !$setupEntry[0].Signed -or $setupEntry[0].SHA256 -ne $setupHash -or (Get-Content -LiteralPath "$setupTarget\$setupName.sha256" -Raw) -ne "$setupHash  $setupName") { throw 'Signing did not refresh the installer checksum and manifest.' }
} finally { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey }

# Mutation check: prove the conflicting-mode regression detects a removed guard.
$originalBuilder = Get-Content -LiteralPath $builder -Raw
try {
    [IO.File]::WriteAllText($builder, $originalBuilder.Replace('if ($BuildOnly -and $SkipBuild)', 'if ($false)'))
    $mutationDetected = $false
    try { Assert-Failure { & $builder -BuildOnly -SkipBuild -BrowserFlavor Modern -Platform x86 -OutputRoot $build } 'Use either' } catch { $mutationDetected = $true }
    if (!$mutationDetected) { throw 'The conflicting-mode test did not catch the mutation.' }
} finally { [IO.File]::WriteAllText($builder, $originalBuilder) }
Write-Output "Release packaging, guards, manifest merge, Git rules and mutation check passed. Offline fixtures: $qaRoot"
