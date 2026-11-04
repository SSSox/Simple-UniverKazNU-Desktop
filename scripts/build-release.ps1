#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Portable','Installer','Both')][string]$Package = 'Both',
    [ValidateSet('Modern','Legacy','all')][string]$BrowserFlavor = 'Modern',
    [ValidateSet('x64','x86','all')][string]$Platform = 'x64',
    [string]$OutputDirectory,
    [switch]$Verify
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install a compatible .NET SDK to build the application.' }
$session = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$buildRoot = Join-Path $projectRoot "build\release-$session"
$dist = if ($OutputDirectory) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory) } else { Join-Path $projectRoot "artifacts\release-$session" }
if (Test-Path -LiteralPath $dist) { throw "Release folder already exists: $dist. Choose a new -OutputDirectory." }
if ($dist.TrimEnd('\').Equals($projectRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -or $dist.StartsWith($buildRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release folder must be separate from source and build output.' }
$parameters = @{ BrowserFlavor=$BrowserFlavor; Platform=$Platform; OutputRoot=$buildRoot; ArtifactRoot=$dist }
Write-Host "Building current source: $BrowserFlavor $Platform; $Package"
& (Join-Path $PSScriptRoot 'build.ps1') @parameters
$manifestFile = @(Get-ChildItem -LiteralPath $dist -Filter '*-manifest.json' -File)
if ($manifestFile.Count -ne 1) { throw 'Expected one portable manifest.' }
$releases = @(Get-Content -LiteralPath $manifestFile[0].FullName -Raw | ConvertFrom-Json)
$checks = @()
if ($Verify) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $unpacked = Join-Path $buildRoot 'verified'
    foreach ($release in $releases) {
        $zip = Join-Path $dist $release.Zip
        if ((Get-FileHash -LiteralPath $zip).Hash -ne $release.SHA256) { throw 'Portable checksum mismatch.' }
        $destination = Join-Path $unpacked "$($release.Flavor)\$($release.Architecture)"
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $destination)
        & (Join-Path $PSScriptRoot 'smoke.ps1') -OutputRoot $unpacked -BrowserFlavor $release.Flavor -Platform $release.Architecture
        $count = @(Select-String -LiteralPath (Join-Path $destination 'self-test.ok') -Pattern '^PASS').Count
        if (!$count) { throw 'Native smoke produced no checks.' }
        $checks += [pscustomobject]@{ Flavor=$release.Flavor; Architecture=$release.Architecture; Passed=$count }
    }
}
if ($Package -ne 'Portable') {
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -ArtifactRoot $dist -BrowserFlavor $BrowserFlavor -Platform $Platform
}
$result = [ordered]@{
    CompletedUtc=[DateTime]::UtcNow.ToString('o'); SourceRoot=$projectRoot; BuildRoot=$buildRoot; ArtifactRoot=$dist
    Package=$Package; BrowserFlavor=$BrowserFlavor; Platform=$Platform; Signed=$false
    ApplicationSmoke= $(if ($Verify) { 'PASS' } else { 'NOT RUN (use -Verify)' }); NativeChecks=$checks
    PortableManifest=$manifestFile[0].Name
}
[IO.File]::WriteAllText((Join-Path $dist 'RELEASE-RESULT.json'), ($result | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "Ready: $dist"
