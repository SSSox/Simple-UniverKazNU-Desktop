param(
    [ValidateSet('x64','x86','all')][string]$Platform = 'all',
    [ValidateSet('modern','legacy','all')][string]$BrowserFlavor = 'all',
    [string]$ArtifactRoot
)
$ErrorActionPreference = 'Stop'
$BrowserFlavor = $BrowserFlavor.ToLowerInvariant()
$Platform = $Platform.ToLowerInvariant()
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'src\Univer.Desktop\Univer.Desktop.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
if (!$ArtifactRoot) { throw 'Specify -ArtifactRoot with the portable release folder, or use scripts/build-release.ps1 to build from current source.' }
$artifactRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactRoot)
$pins = Get-Content -LiteralPath (Join-Path $projectRoot 'installer\prerequisites.json') -Raw | ConvertFrom-Json
$releaseManifest = Join-Path $artifactRoot "Univer-Desktop-$version-manifest.json"
if (!(Test-Path -LiteralPath $releaseManifest)) { throw 'Portable manifest not found. Run scripts/build.ps1 first and use the same -ArtifactRoot.' }
$releases = @(Get-Content -LiteralPath $releaseManifest -Raw | ConvertFrom-Json)
$flavors = if ($BrowserFlavor -eq 'all') { @('modern','legacy') } else { @($BrowserFlavor) }
$platforms = if ($Platform -eq 'all') { @('x64','x86') } else { @($Platform) }
$manifestPath = Join-Path $artifactRoot "Univer-Desktop-$version-installers.json"
$manifest = @()
if (Test-Path -LiteralPath $manifestPath) { $manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json) }
foreach ($flavor in $flavors) {
    foreach ($arch in $platforms) {
        $release = @($releases | Where-Object { $_.Flavor -eq $flavor -and $_.Architecture -eq $arch })
        if ($release.Count -ne 1) { throw "Missing portable release: $flavor $arch. Run scripts/build.ps1 for this variant first." }
        if ($release[0].Version -ne $version -or $release[0].Zip -cne "Univer-Desktop-$version-$flavor-$arch.zip" -or $release[0].SHA256 -notmatch '^[0-9A-F]{64}$' -or $release[0].ExeSHA256 -notmatch '^[0-9A-F]{64}$') { throw "Invalid portable manifest entry: $flavor $arch" }
        $setupFile = Join-Path $artifactRoot "Univer-Desktop-$version-$flavor-$arch-setup.exe"
        if ((Test-Path -LiteralPath $setupFile) -or (Test-Path -LiteralPath "$setupFile.sha256") -or @($manifest | Where-Object { $_.Flavor -eq $flavor -and $_.Architecture -eq $arch }).Count) { throw "Installer already exists: $setupFile. Choose a new -ArtifactRoot for portable and setup." }
    }
}
$compiler = & (Join-Path $PSScriptRoot 'prepare-installer-tools.ps1')
$buildSession = [Guid]::NewGuid().ToString('N')
foreach ($flavor in $flavors) {
    foreach ($arch in $platforms) {
        $release = @($releases | Where-Object { $_.Flavor -eq $flavor -and $_.Architecture -eq $arch })
        if ($release.Count -ne 1) { throw "Missing verified release: $flavor $arch" }
        $zip = Join-Path $artifactRoot $release[0].Zip
        if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $release[0].SHA256) { throw "Release checksum mismatch: $zip" }
        $buildRoot = Join-Path $projectRoot "build\installer-$version-$buildSession\$flavor-$arch"
        $payload = Join-Path $buildRoot 'payload'
        # Each build gets a fresh staging directory; never consume a running app or mutable portable folder.
        if (Test-Path -LiteralPath $buildRoot) { throw "Installer staging already exists: $buildRoot" }
        $setupFile = Join-Path $artifactRoot "Univer-Desktop-$version-$flavor-$arch-setup.exe"
        if (Test-Path -LiteralPath $setupFile) { throw "Installer already exists: $setupFile" }
        New-Item -ItemType Directory -Path $payload -Force | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $payload)
        if ((Get-FileHash -LiteralPath (Join-Path $payload 'Univer.Desktop.exe') -Algorithm SHA256).Hash -ne $release[0].ExeSHA256) { throw 'Executable checksum mismatch.' }
        $forbidden = @(Get-ChildItem -LiteralPath $payload -Recurse -Force | Where-Object { $_.Name -eq 'qa' -or $_.Name -like 'self-test.*' -or $_.Extension -in @('.log','.pdb','.dpapi') -or $_.Name -in @('credentials.bin','preferences.json','downloads.json','installation-id.txt','Browser','Browser-Modern','DawnCache','GPUCache','Cookies','Local State','AGENTS.md','SKILL.md','SKILLS.md','CLAUDE.md','GEMINI.md','.agents','.codex','.claude','.cursor') })
        if ($forbidden.Count) { throw 'Private or QA files found in release.' }
        $netName = if ($flavor -eq 'legacy') { 'net452' } else { 'net462' }
        $net = @($pins | Where-Object Name -eq $netName)
        $vc = @($pins | Where-Object Name -eq "$flavor-$arch")
        foreach ($pin in @($net) + @($vc)) {
            if ($pin.SHA256 -notmatch '^[0-9A-F]{64}$' -or $pin.Url -notmatch '^https://(download\.microsoft\.com|download\.visualstudio\.microsoft\.com)/[A-Za-z0-9._/%-]+$' -or $pin.Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid prerequisite pin.' }
        }
        if ($net.Count -ne 1 -or $vc.Count -ne 1) { throw 'Prerequisite pin is missing or duplicated.' }
        $markerFile = Join-Path $buildRoot 'installation-id.txt'
        $infoFile = Join-Path $buildRoot 'before.txt'
        [IO.File]::WriteAllText($markerFile, "Univer.Desktop.$flavor.$arch", [Text.UTF8Encoding]::new($false))
        $info = if ($flavor -eq 'legacy') {
            "Univer Legacy: версия для Windows 7 SP1, 8 и 8.1.`r`n`r`nВ этой версии используется устаревший браузер с известными уязвимостями. Для Windows 10 и 11 используйте Univer Modern.`r`n`r`nПредупреждение также доступно в настройках приложения.`r`n`r`nОбновление и удаление приложения сохраняют данные входа и настройки текущего пользователя Windows.`r`n`r`nНеофициальное приложение: не связано с КазНУ им. аль-Фараби. Вход и данные остаются на сайте univer.kaznu.kz."
        } else {
            "Univer Modern: версия для Windows 10 и 11.`r`n`r`nУстановка выполняется для текущего пользователя Windows.`r`n`r`nОбновление и удаление приложения сохраняют данные входа и настройки.`r`n`r`nНеофициальное приложение: не связано с КазНУ им. аль-Фараби. Вход и данные остаются на сайте univer.kaznu.kz."
        }
        [IO.File]::WriteAllText($infoFile, $info, [Text.UTF8Encoding]::new($true))
        $arguments = @('/Qp', "/DAppVersion=$version", "/DFlavor=$flavor", "/DArch=$arch", "/DPayloadRoot=$payload", "/DOutputRoot=$artifactRoot", "/DMarkerFile=$markerFile", "/DInfoFile=$infoFile", "/DNetUrl=$($net[0].Url)", "/DNetSHA256=$($net[0].SHA256)", "/DVCUrl=$($vc[0].Url)", "/DVCSHA256=$($vc[0].SHA256)", "/DVCVersion=$($vc[0].Version)", (Join-Path $projectRoot 'installer\Univer.iss'))
        & $compiler @arguments
        if ($LASTEXITCODE -ne 0) { throw "Installer build failed: $flavor $arch" }
        $hash = (Get-FileHash -LiteralPath $setupFile -Algorithm SHA256).Hash
        [IO.File]::WriteAllText("$setupFile.sha256", "$hash  $([IO.Path]::GetFileName($setupFile))", [Text.UTF8Encoding]::new($false))
        $manifest += [pscustomobject]@{
            Flavor=$flavor; Architecture=$arch; Version=$version
            Installer=[IO.Path]::GetFileName($setupFile); Bytes=(Get-Item -LiteralPath $setupFile).Length; SHA256=$hash
            PayloadZipSHA256=$release[0].SHA256; Compiler='Inno Setup 7.1.0 x86'; Signed=$false
            InstallerSourceSHA256=(Get-FileHash (Join-Path $projectRoot 'installer\Univer.iss') -Algorithm SHA256).Hash
            KazakhTranslationSHA256=(Get-FileHash (Join-Path $projectRoot 'installer\Kazakh.isl') -Algorithm SHA256).Hash
            Languages=@('russian','english','kazakh')
        }
        [IO.File]::WriteAllText("$manifestPath.tmp", (ConvertTo-Json -InputObject @($manifest) -Depth 4), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath "$manifestPath.tmp" -Destination $manifestPath -Force
        Write-Output "Built $setupFile"
    }
}
