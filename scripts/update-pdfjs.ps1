# Vendors the pinned PDF.js legacy build (SHA-512 checked) and only the data files the reader uses into Assets\pdfjs.
# Stay on 5.7: 6.x needs Response.bytes() and AbortSignal.any(), which Chromium 109 (Legacy) lacks.
param(
    [string]$Version = '5.7.284',
    [string]$Integrity = 'sha512-h4EdYQczmGhbOlqc3PPZwxevn7ApdWPbovAuWXOB/DjIyigSnwfy2oze7c6mRcSr9XgLp3eN3EeL4DyySTPMFw=='
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$target = Join-Path $projectRoot 'src\Univer.Desktop\Assets\pdfjs'
$work = Join-Path ([IO.Path]::GetTempPath()) ('univer-pdfjs-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Push-Location -LiteralPath $work
    try { & npm.cmd pack "pdfjs-dist@$Version" --silent | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'npm pack failed' } }
    finally { Pop-Location }
    $tarball = Join-Path $work "pdfjs-dist-$Version.tgz"
    $hash = [Security.Cryptography.SHA512]::Create()
    $stream = [IO.File]::OpenRead($tarball)
    try { $actual = 'sha512-' + [Convert]::ToBase64String($hash.ComputeHash($stream)) } finally { $stream.Dispose(); $hash.Dispose() }
    if ($actual -ne $Integrity) { throw "pdfjs-dist $Version does not match the pinned integrity: $actual" }
    $extracted = Join-Path $work 'extracted'
    New-Item -ItemType Directory -Path $extracted | Out-Null
    & tar.exe -xzf $tarball -C $extracted
    if ($LASTEXITCODE -ne 0) { throw 'Extraction failed' }
    $package = Join-Path $extracted 'package'
    if ((Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json).version -ne $Version) { throw 'Unexpected package version' }
    $files = @(
        'LICENSE',
        'legacy\build\pdf.min.mjs',
        'legacy\build\pdf.worker.min.mjs',
        'iccs\CGATS001Compat-v2-micro.icc', 'iccs\LICENSE',
        'wasm\jbig2.wasm', 'wasm\jbig2_nowasm_fallback.js', 'wasm\openjpeg.wasm', 'wasm\openjpeg_nowasm_fallback.js', 'wasm\qcms_bg.wasm',
        'wasm\LICENSE_JBIG2', 'wasm\LICENSE_OPENJPEG', 'wasm\LICENSE_PDFJS_JBIG2', 'wasm\LICENSE_PDFJS_OPENJPEG', 'wasm\LICENSE_PDFJS_QCMS', 'wasm\LICENSE_QCMS',
        'standard_fonts\LICENSE_FOXIT'
    )
    $files += @(Get-ChildItem -LiteralPath (Join-Path $package 'standard_fonts') -Filter 'Foxit*.pfb' -File | ForEach-Object { 'standard_fonts\' + $_.Name })
    $files += @(Get-ChildItem -LiteralPath (Join-Path $package 'cmaps') -File | ForEach-Object { 'cmaps\' + $_.Name })
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    foreach ($file in $files) {
        $source = Join-Path $package $file
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing from the package: $file" }
        # The legacy build sits at the root of the vendored folder; data folders keep their names.
        $relative = if ($file.StartsWith('legacy\build\')) { $file.Substring(13) } else { $file }
        $destination = Join-Path $target $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    $note = "pdfjs-dist $Version (Mozilla PDF.js, Apache-2.0), legacy build`r`nSource: https://registry.npmjs.org/pdfjs-dist/-/pdfjs-dist-$Version.tgz`r`nIntegrity: $Integrity`r`nUpdated with scripts\update-pdfjs.ps1; do not edit these files by hand.`r`n"
    [IO.File]::WriteAllText((Join-Path $target 'VERSION.txt'), $note, (New-Object Text.UTF8Encoding($false)))
    $count = @(Get-ChildItem -LiteralPath $target -File -Recurse).Count
    $bytes = (Get-ChildItem -LiteralPath $target -File -Recurse | Measure-Object Length -Sum).Sum
    Write-Output "PDF.js $Version vendored: $count files, $bytes bytes in $target"
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
