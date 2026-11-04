param(
    [ValidateSet('x64','x86','all')][string]$Platform = 'all',
    [ValidateSet('Modern','Legacy','all')][string]$BrowserFlavor = 'all',
    [switch]$BuildOnly, [switch]$SkipBuild, [string]$OutputRoot, [string]$ArtifactRoot
)
$ErrorActionPreference = 'Stop'
if ($BuildOnly -and $SkipBuild) { throw 'Use either -BuildOnly or -SkipBuild.' }
$Platform = $Platform.ToLowerInvariant()
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $projectRoot 'src\Univer.Desktop\Univer.Desktop.csproj'
$artifactRoot = if ($ArtifactRoot) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactRoot) } else { Join-Path $projectRoot 'artifacts' }
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Project version not found' }
$platforms = if ($Platform -eq 'all') { @('x64','x86') } else { @($Platform) }
$flavors = if ($BrowserFlavor -eq 'all') { @('Modern','Legacy') } elseif ($BrowserFlavor -eq 'Legacy') { @('Legacy') } else { @('Modern') }
function Get-BuildOutput([string]$Flavor, [string]$Arch) {
    if ($OutputRoot) { return (Join-Path ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)) "$($Flavor.ToLowerInvariant())\$Arch") }
    $framework = if ($Flavor -eq 'Legacy') { 'net452' } else { 'net462' }
    return (Join-Path $projectRoot "src\Univer.Desktop\bin\$Flavor\$Arch\Release\$framework")
}
function Assert-CurrentAssets([string]$Output) {
    $source = Join-Path $projectRoot 'src\Univer.Desktop\Assets'
    $target = Join-Path $Output 'Assets'
    $sourceFiles = @(Get-ChildItem -LiteralPath $source -File -Recurse)
    $targetFiles = @(Get-ChildItem -LiteralPath $target -File -Recurse)
    if ($sourceFiles.Count -ne $targetFiles.Count) { throw 'Assets differ from current source. Rebuild into a fresh -OutputRoot before packaging.' }
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($source.Length + 1)
        $copy = Join-Path $target $relative
        if (!(Test-Path -LiteralPath $copy) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash) {
            throw "Assets differ from current source: $relative. Rebuild before packaging; -SkipBuild does not update the interface."
        }
    }
}
# The project ships only the Chromium locales the app selects (ru, en-GB and the en-US fallback) and no WebGPU compiler.
function Assert-TrimmedRuntime([string]$Output) {
    $locales = @(Get-ChildItem -LiteralPath (Join-Path $Output 'locales') -Filter '*.pak' -File | ForEach-Object { $_.BaseName.Split('_')[0] } | Sort-Object -Unique)
    if (($locales -join ',') -ne 'en-GB,en-US,ru') { throw "Unexpected Chromium locales in ${Output}: $($locales -join ','). Rebuild into a fresh -OutputRoot." }
    if ((Test-Path -LiteralPath (Join-Path $Output 'dxcompiler.dll')) -or (Test-Path -LiteralPath (Join-Path $Output 'dxil.dll'))) { throw "Untrimmed Chromium runtime in $Output." }
}
$manifestPath = Join-Path $artifactRoot "Univer-Desktop-$version-manifest.json"
$manifest = @()
if (!$BuildOnly) {
    if (Test-Path -LiteralPath $manifestPath) { $manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json) }
    # Check every destination before building; never overwrite a published release.
    foreach ($flavor in $flavors) {
        foreach ($arch in $platforms) {
            $output = Get-BuildOutput $flavor $arch
            if ($artifactRoot.TrimEnd('\') -eq $output.TrimEnd('\') -or $artifactRoot.StartsWith($output.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'ArtifactRoot must be outside the application build output.' }
            $packagePath = Join-Path $artifactRoot "Univer-Desktop-$version-$($flavor.ToLowerInvariant())-$arch"
            if ((Test-Path -LiteralPath $packagePath) -or (Test-Path -LiteralPath "$packagePath.zip") -or (Test-Path -LiteralPath "$packagePath.zip.sha256") -or @($manifest | Where-Object { $_.Flavor -eq $flavor -and $_.Architecture -eq $arch }).Count) {
                throw "Package already exists: $packagePath. Choose a new -ArtifactRoot."
            }
        }
    }
    New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
}
foreach ($flavor in $flavors) {
    $engine = if ($flavor -eq 'Legacy') { '109.1.110' } else { '152.0.150' }
    foreach ($arch in $platforms) {
        $output = Get-BuildOutput $flavor $arch
        if (!$SkipBuild) {
            $buildArgs = @('build', $project, '-c', 'Release', "-p:Platform=$arch", "-p:BrowserFlavor=$flavor", '-p:RestoreLockedMode=true', '--verbosity', 'quiet')
            if ($OutputRoot) { $buildArgs += @('-o', $output) }
            & dotnet @buildArgs
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $flavor $arch" }
        }
        $builtExe = Join-Path $output 'Univer.Desktop.exe'
        if (!(Test-Path -LiteralPath $builtExe) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($builtExe).FileVersion -ne "$version.0") { throw "Expected built $version executable: $flavor $arch" }
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'CefSharp.dll')).FileVersion -ne "$engine.0") { throw "Wrong browser output: $flavor $arch" }
        $nativeFile = [IO.File]::OpenRead((Join-Path $output 'libcef.dll'))
        try {
            $reader = New-Object IO.BinaryReader($nativeFile)
            $nativeFile.Position = 0x3c; $peOffset = $reader.ReadInt32()
            $nativeFile.Position = $peOffset
            if ($reader.ReadUInt32() -ne 0x00004550) { throw 'Invalid native PE header' }
            $expectedMachine = if ($arch -eq 'x64') { 0x8664 } else { 0x014c }
            if ($reader.ReadUInt16() -ne $expectedMachine) { throw "Wrong native architecture: $flavor $arch" }
        } finally { $nativeFile.Dispose() }
        Assert-CurrentAssets $output
        Assert-TrimmedRuntime $output
        if ($BuildOnly) {
            Write-Output "Built $flavor ${arch}: $output"
            continue
        }
        $privateFiles = @(Get-ChildItem -LiteralPath $output -Recurse -Force | Where-Object { $_.Extension -eq '.dpapi' -or $_.Name -in @('credentials.bin','preferences.json','downloads.json','installation-id.txt','Browser','Browser-Modern','DawnCache','GPUCache','Cookies','Local State') })
        if ($privateFiles.Count) { throw "Local account or browser data found in build output: $flavor $arch. Use a clean build folder." }
        $packagePath = Join-Path $artifactRoot "Univer-Desktop-$version-$($flavor.ToLowerInvariant())-$arch"
        $zip = "$packagePath.zip"
        # Fresh destinations prevent stale DLLs from a previous engine from entering the package.
        if ((Test-Path -LiteralPath $packagePath) -or (Test-Path -LiteralPath $zip)) { throw "Package already exists: $packagePath. Use a fresh release destination." }
        New-Item -ItemType Directory -Path $packagePath | Out-Null
        Get-ChildItem -LiteralPath $output -Force | Where-Object { $_.Name -notin @('qa','qa-study') -and $_.Name -notlike 'self-test.*' -and $_.Name -notlike 'qa-keys.*' -and $_.Name -notlike '*.log' -and $_.Name -notlike '*.pdb' -and $_.Name -notlike '*.xml' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $packagePath -Recurse -Force
        }
        # The same public documents as the repository, in Russian, Kazakh and English, so README links work in the package too.
        Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packagePath
        foreach ($document in @('README','SECURITY','PRIVACY','BUILD','THIRD-PARTY')) {
            foreach ($language in @('.md','.kz.md','.en.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot "$document$language") -Destination $packagePath }
        }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $packagePath -Recurse
        $requirements = if ($flavor -eq 'Legacy') { @"
Windows 7 SP1 / 8 / 8.1; .NET Framework 4.5.2+; Visual C++ 2019 ($arch).
WARNING: Chromium 109 is unsupported and contains known vulnerabilities. Use Modern on Windows 10/11.
"@ } else { "Windows 10 / 11; .NET Framework 4.6.2+; Visual C++ 2022 ($arch)." }
        $signing = if ((Get-AuthenticodeSignature -LiteralPath (Join-Path $packagePath 'Univer.Desktop.exe')).SignerCertificate) { 'Signed' } else { 'Unsigned' }
        $info = @"
Univer Desktop $version $flavor $arch
CefSharp $engine
$requirements
$signing portable. Extract the complete ZIP, then run Univer.Desktop.exe.
Unofficial application, not affiliated with Al-Farabi Kazakh National University.
"@
        [IO.File]::WriteAllText((Join-Path $packagePath 'BUILD-INFO.txt'), $info, (New-Object Text.UTF8Encoding($false)))
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($packagePath, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
        [IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))", (New-Object Text.UTF8Encoding($false)))
        $manifest += [pscustomobject]@{
            Flavor=$flavor.ToLowerInvariant(); Architecture=$arch; Version=$version
            Zip=[IO.Path]::GetFileName($zip); Bytes=(Get-Item -LiteralPath $zip).Length; SHA256=$hash
            ExeSHA256=(Get-FileHash -LiteralPath (Join-Path $packagePath 'Univer.Desktop.exe') -Algorithm SHA256).Hash; Signed=($signing -eq 'Signed')
        }
        # Persist each completed variant, including when a later build fails.
        [IO.File]::WriteAllText("$manifestPath.tmp", (ConvertTo-Json -InputObject @($manifest) -Depth 4), [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath "$manifestPath.tmp" -Destination $manifestPath -Force
        Write-Output "$flavor $arch ZIP: $zip ($((Get-Item -LiteralPath $zip).Length) bytes), SHA256: $hash"
    }
}
