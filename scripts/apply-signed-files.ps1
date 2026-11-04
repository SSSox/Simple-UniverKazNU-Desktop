# Puts files signed by SignPath over their unsigned originals and keeps installer checksums and the manifest current.
param(
    [Parameter(Mandatory=$true)][string]$SignedRoot,
    [Parameter(Mandatory=$true)][string]$TargetRoot,
    [switch]$TestCertificate
)
$ErrorActionPreference = 'Stop'
$signedRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SignedRoot).TrimEnd('\')
$targetRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($TargetRoot).TrimEnd('\')
$files = @(Get-ChildItem -LiteralPath $signedRoot -File -Recurse)
if (!$files.Count) { throw "No signed files in $signedRoot." }
# Every file is checked before the first copy, so a bad result leaves the unsigned build whole.
foreach ($file in $files) {
    $relative = $file.FullName.Substring($signedRoot.Length + 1)
    if (!(Test-Path -LiteralPath (Join-Path $targetRoot $relative) -PathType Leaf)) { throw "Signed file has no unsigned original: $relative" }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    if (!$signature.SignerCertificate) { throw "Not signed: $relative" }
    if (!$TestCertificate -and ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'SignPath Foundation')) { throw "Unexpected signature on ${relative}: $($signature.Status), $($signature.SignerCertificate.Subject)" }
}
$utf8 = New-Object Text.UTF8Encoding($false)
foreach ($file in $files) {
    $target = Join-Path $targetRoot $file.FullName.Substring($signedRoot.Length + 1)
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    Write-Output "Signed: $target"
    if ($file.Name -notlike '*-setup.exe') { continue }
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    [IO.File]::WriteAllText("$target.sha256", "$hash  $($file.Name)", $utf8)
    $manifestFile = @(Get-ChildItem -LiteralPath (Split-Path $target) -Filter '*-installers.json' -File)
    if ($manifestFile.Count -ne 1) { throw 'Expected one installer manifest.' }
    $manifest = @(Get-Content -LiteralPath $manifestFile[0].FullName -Raw | ConvertFrom-Json)
    $entry = @($manifest | Where-Object { $_.Installer -ceq $file.Name })
    if ($entry.Count -ne 1) { throw "Installer is not in the manifest: $($file.Name)" }
    $entry[0].SHA256 = $hash
    $entry[0].Bytes = $file.Length
    $entry[0].Signed = $true
    [IO.File]::WriteAllText($manifestFile[0].FullName, (ConvertTo-Json -InputObject @($manifest) -Depth 4), $utf8)
}
