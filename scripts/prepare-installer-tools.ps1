$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolRoot = Join-Path $projectRoot 'tools\inno-7.1.0'
$compiler = Join-Path $toolRoot 'compiler\ISCC.exe'
$setup = Join-Path $toolRoot 'innosetup-7.1.0-x86.exe'
$expectedHash = 'F9671174E0D15BA9B4F6B56564C6AED32EA8DB9C3CB9BF6F2AF0850FE7894F60'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
if (!(Test-Path -LiteralPath $setup)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x86.exe' -OutFile $setup
}
if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno Setup checksum mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $setup
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Invalid Inno Setup publisher signature.' }
if (!(Test-Path -LiteralPath $compiler)) {
    $destination = Join-Path $toolRoot 'compiler'
    $process = Start-Process -FilePath $setup -ArgumentList @('/PORTABLE=1', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', '/NOICONS', '/TASKS=""', ('/DIR="' + $destination + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Inno Setup extraction failed: $($process.ExitCode)" }
}
$compilerSignature = Get-AuthenticodeSignature -LiteralPath $compiler
if ($compilerSignature.Status -ne 'Valid' -or $compilerSignature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Invalid compiler publisher signature.' }
if ((& $compiler --version).Trim() -ne '7.1.0') { throw 'Unexpected Inno Setup version.' }
Write-Output $compiler
