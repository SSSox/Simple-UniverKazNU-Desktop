$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Read-LanguageFile([string]$Path) {
    $result = @{}
    $section = ''
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\[(.+)\]$') { $section = $Matches[1] }
        elseif ($section -in @('Messages','CustomMessages') -and $line -match '^([A-Za-z0-9]+)=(.*)$') {
            $key = "$section.$($Matches[1])"
            if ($result.ContainsKey($key)) { throw "Duplicate language key: $key" }
            $result[$key] = $Matches[2]
        }
    }
    return $result
}
function Get-Placeholders([string]$Text, [string]$Pattern) {
    return (([regex]::Matches($Text,$Pattern) | ForEach-Object Value | Sort-Object) -join '|')
}
# Inno Setup's own English messages are the reference; a fresh clone fetches and verifies the compiler first.
$compiler = & (Join-Path $PSScriptRoot 'prepare-installer-tools.ps1')
$english = Read-LanguageFile (Join-Path (Split-Path -Parent $compiler) 'Default.isl')
$kazakh = Read-LanguageFile (Join-Path $projectRoot 'installer\Kazakh.isl')
foreach ($key in $english.Keys) {
    if (!$kazakh.ContainsKey($key)) { throw "Kazakh installer translation missing: $key" }
    $pattern = '%(?:\d+|n)|\[(?:name/ver|name|gb|mb)\]'
    if ((Get-Placeholders $english[$key] $pattern) -ne (Get-Placeholders $kazakh[$key] $pattern)) { throw "Installer placeholder mismatch: $key" }
    if ($english[$key] -and !$kazakh[$key]) { throw "Empty Kazakh message: $key" }
}
Write-Output "PASS Kazakh installer: $($english.Count) message keys and placeholder contracts"
$custom = @{russian=@{};english=@{};kazakh=@{}}
foreach ($line in Get-Content -LiteralPath (Join-Path $projectRoot 'installer\Univer.iss')) {
    if ($line -match '^(russian|english|kazakh)\.([A-Za-z0-9]+)=(.*)$') { $custom[$Matches[1]][$Matches[2]] = $Matches[3] }
}
foreach ($language in @('russian','kazakh')) {
    foreach ($key in $custom.english.Keys) {
        if (!$custom[$language].ContainsKey($key)) { throw "Custom translation missing: $language $key" }
        if ((Get-Placeholders $custom.english[$key] '%(?:\d+|n)') -ne (Get-Placeholders $custom[$language][$key] '%(?:\d+|n)')) { throw "Custom placeholder mismatch: $language $key" }
    }
}
Write-Output "PASS installer-specific messages: $($custom.english.Count) keys in Russian, Kazakh and English"
$stringsFile = Join-Path $projectRoot 'src\Univer.Desktop\UI\Strings.cs'
$type = Add-Type -TypeDefinition (Get-Content -LiteralPath $stringsFile -Raw) -PassThru | Where-Object FullName -eq 'Univer.Desktop.UI.Strings'
$field = $type.GetField('values',[Reflection.BindingFlags]'NonPublic,Static')
$native = $field.GetValue($null)
foreach ($entry in $native.GetEnumerator()) {
    if ($entry.Value.Length -ne 3) { throw "Wrong language count: $($entry.Key)" }
    foreach ($translation in $entry.Value) {
        if ([string]::IsNullOrWhiteSpace($translation)) { throw "Empty application translation: $($entry.Key)" }
        if ((Get-Placeholders $entry.Value[0] '\{\d+\}') -ne (Get-Placeholders $translation '\{\d+\}')) { throw "Application placeholder mismatch: $($entry.Key)" }
    }
}
foreach ($file in Get-ChildItem (Join-Path $projectRoot 'src\Univer.Desktop') -Filter '*.cs' -Recurse) {
    foreach ($use in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw),'Strings\.Get\("([^"]+)"\s*,')) {
        if (!$native.ContainsKey($use.Groups[1].Value)) { throw "Unknown application label: $($use.Groups[1].Value)" }
    }
    # Status keys reach the banner through UiStatus and are looked up by value, not by a literal Strings.Get call.
    foreach ($use in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw),'(?:SetStatus|\breport|\bstatus)\("([A-Za-z]+)"\)')) {
        if (!$native.ContainsKey($use.Groups[1].Value)) { throw "Untranslated status: $($use.Groups[1].Value) in $($file.Name)" }
    }
}
Write-Output "PASS application: $($native.Count) keys in Russian, Kazakh and English; static callers resolve"
