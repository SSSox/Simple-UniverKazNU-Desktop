<#
Builds the browser extension for Chrome/Edge (chromium) and Firefox from src/Univer.Extension and the page scripts the
Windows app injects (src/Univer.Desktop/Assets, in the order of Web/AutomationController.cs, unchanged). Each page script
is its own content script in the same strict wrapper the Windows app uses, so one failing script cannot stop the others.

  .\scripts\build-extension.ps1          release: artifacts\extension\{chromium,firefox} and the two ZIP files
  .\scripts\build-extension.ps1 -Qa      QA only: also matches $QaOrigin (local fixture pages), no ZIP files
#>
[CmdletBinding()]
param(
    [switch]$Qa,
    [string]$QaOrigin = 'http://127.0.0.1:8765',
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'src\Univer.Extension'
$assets = Join-Path $root 'src\Univer.Desktop\Assets'
$controller = Join-Path $root 'src\Univer.Desktop\Web\AutomationController.cs'
if (-not $OutputRoot) { $OutputRoot = Join-Path $root $(if ($Qa) { 'artifacts\extension-qa' } else { 'artifacts\extension' }) }
# One full path for cmdlets and .NET alike: .NET resolves a relative path against the process directory, not the
# PowerShell location, and ZIP entry names are cut from full paths.
$OutputRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot).TrimEnd('\')
$origin = 'https://univer.kaznu.kz'
$literal = "'$origin'"
$utf8 = New-Object Text.UTF8Encoding($false)

function Read-Text([string]$path) { [IO.File]::ReadAllText($path, $utf8) }
function Write-Text([string]$path, [string]$value) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [IO.File]::WriteAllText($path, $value, $utf8)
}
# The scripts name the origin themselves (Core/ScriptBundle.cs); a QA build points them at the local fixture server.
function Set-Origin([string]$value) { if ($Qa) { $value.Replace($literal, "'$QaOrigin'") } else { $value } }
function Wrap([string]$value, [string]$sender) { "(function(__univerNative){'use strict';`n" + $value + "`n})(" + $sender + ");`n" }

# The one list of page scripts, read from the Windows app so the two cannot drift apart.
$text = Read-Text $controller
$match = [regex]::Match($text, 'Bundle = new Lazy<string>\(\(\) => Scripts\(([^)]*)\)\)')
if (-not $match.Success) { throw 'The page script list was not found in Web/AutomationController.cs.' }
$modules = @([regex]::Matches($match.Groups[1].Value, '"([^"]+\.js)"') | ForEach-Object { $_.Groups[1].Value })
# Needs the site's own jQuery and agree(), which only the page's world (MAIN) can see.
$pageModules = @('univer-automation.js')
$isolated = @($modules | Where-Object { $pageModules -notcontains $_ })
$own = @('host.js', 'downloads.js', 'driver.js') | Where-Object { Test-Path -LiteralPath (Join-Path $source "scripts\$_") }
$manifestSource = Read-Text (Join-Path $source 'manifest.json') | ConvertFrom-Json
$version = $manifestSource.version
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Unexpected extension version: $version" }

function Build-Browser([string]$browser) {
    $out = Join-Path $OutputRoot $browser
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    foreach ($folder in '_locales', 'icons', 'popup') {
        $from = Join-Path $source $folder
        if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $out $folder) -Recurse }
    }
    Write-Text (Join-Path $out 'background.js') (Set-Origin (Read-Text (Join-Path $source 'background.js')))
    Write-Text (Join-Path $out 'scripts\prelude.js') (Set-Origin (Read-Text (Join-Path $source 'scripts\prelude.js')))
    $isolatedSender = 'globalThis.__univerExtV1?globalThis.__univerExtV1.send:null'
    foreach ($name in $isolated) {
        Write-Text (Join-Path $out "scripts\modules\$name") (Wrap (Set-Origin (Read-Text (Join-Path $assets $name))) $isolatedSender)
    }
    foreach ($name in $own) { Write-Text (Join-Path $out "scripts\$name") (Set-Origin (Read-Text (Join-Path $source "scripts\$name"))) }
    # MAIN world: the page-side bridge (when present) holds the sender in its own closure; otherwise the module gets none.
    $bridgePath = Join-Path $source 'scripts\page-bridge.js'
    $pageScript = ''
    foreach ($name in $pageModules) { $pageScript += Read-Text (Join-Path $assets $name) }
    if (Test-Path -LiteralPath $bridgePath) { $page = (Read-Text $bridgePath).Replace('/*@MODULES@*/', (Wrap $pageScript '__univerPageSend')) }
    else { $page = Wrap $pageScript 'null' }
    Write-Text (Join-Path $out 'scripts\page.js') (Set-Origin $page)

    $sites = @("$origin/*")
    # Match patterns take no port (Firefox rejects one); the QA build's scripts still check the full origin.
    if ($Qa) { $sites += ([regex]::Replace($QaOrigin, ':\d+$', '') + '/*') }
    $manifest = $manifestSource | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $manifest.host_permissions = $sites
    $isolatedScripts = @('scripts/prelude.js') + @($isolated | ForEach-Object { "scripts/modules/$_" }) + @($own | ForEach-Object { "scripts/$_" })
    $manifest | Add-Member -NotePropertyName content_scripts -NotePropertyValue @(
        [ordered]@{ matches = $sites; run_at = 'document_start'; all_frames = $true; match_about_blank = $true; js = $isolatedScripts },
        [ordered]@{ matches = $sites; run_at = 'document_start'; all_frames = $true; match_about_blank = $true; world = 'MAIN'; js = @('scripts/page.js') }
    )
    if ($browser -eq 'firefox') {
        $manifest | Add-Member -NotePropertyName background -NotePropertyValue ([ordered]@{ scripts = @('background.js') })
        $manifest | Add-Member -NotePropertyName browser_specific_settings -NotePropertyValue ([ordered]@{
            gecko = [ordered]@{ id = 'simple-univer@sox-team'; strict_min_version = '128.0'; data_collection_permissions = [ordered]@{ required = @('none') } }
            gecko_android = [ordered]@{ strict_min_version = '128.0' }
        })
    } else {
        $manifest | Add-Member -NotePropertyName background -NotePropertyValue ([ordered]@{ service_worker = 'background.js' })
        $manifest | Add-Member -NotePropertyName minimum_chrome_version -NotePropertyValue '111'
    }
    Write-Text (Join-Path $out 'manifest.json') ($manifest | ConvertTo-Json -Depth 20)
    return $out
}

function Write-Zip([string]$folder, [string]$zip) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        # Forward slashes: stores and Firefox reject backslash entry names.
        $base = (Get-Item -LiteralPath $folder).FullName.TrimEnd('\') + '\'
        Get-ChildItem -LiteralPath $folder -Recurse -File | Sort-Object FullName | ForEach-Object {
            if (-not $_.FullName.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected file outside ${folder}: $($_.FullName)" }
            $name = $_.FullName.Substring($base.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))", $utf8)
    [pscustomobject]@{ File = $zip; Bytes = (Get-Item -LiteralPath $zip).Length; SHA256 = $hash }
}

$built = foreach ($browser in 'chromium', 'firefox') { Build-Browser $browser }
if ($Qa) { $built; return }
foreach ($browser in 'chromium', 'firefox') {
    Write-Zip (Join-Path $OutputRoot $browser) (Join-Path (Split-Path -Parent $OutputRoot) "Univer-Extension-$version-$browser.zip")
}
