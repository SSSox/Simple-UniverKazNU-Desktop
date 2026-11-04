$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$qaRoot = Join-Path $projectRoot ('build\installer-download-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qaRoot | Out-Null
$compiler = & (Join-Path $PSScriptRoot 'prepare-installer-tools.ps1')
$pin = Get-Content -LiteralPath (Join-Path $projectRoot 'installer\prerequisites.json') -Raw | ConvertFrom-Json | Where-Object Name -eq 'modern-x86'
$resultFile = Join-Path $qaRoot 'rejected.txt'
$source = @"
[Setup]
AppName=Univer download regression fixture
AppVersion=1.0
AppId=Univer.Download.QA.$([IO.Path]::GetFileName($qaRoot))
DefaultDirName={localappdata}\Univer.Download.QA
PrivilegesRequired=lowest
OutputDir=$qaRoot
OutputBaseFilename=download-check
Uninstallable=no
[Code]
function InitializeSetup: Boolean;
begin
  Result := False;
  try
    DownloadTemporaryFile('$($pin.Url)', 'microsoft.exe', '0000000000000000000000000000000000000000000000000000000000000000', nil);
    SaveStringToFile('$resultFile', 'UNSAFE: mismatched file accepted', False);
  except
    SaveStringToFile('$resultFile', AnsiString(GetExceptionMessage), False);
  end;
end;
"@
$script = Join-Path $qaRoot 'download.iss'
[IO.File]::WriteAllText($script, $source, [Text.UTF8Encoding]::new($true))
& $compiler /Qp $script
if ($LASTEXITCODE -ne 0) { throw 'Download fixture compilation failed.' }
$process = Start-Process -FilePath (Join-Path $qaRoot 'download-check.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(60000)) { throw 'Download fixture timed out; no component was executed.' }
$result = Get-Content -LiteralPath $resultFile -Raw
if ($result -notmatch '(?i)(hash|checksum).*(match|incorrect)|match.*(hash|checksum)') { throw "Unexpected download rejection: $result" }
Write-Output 'PASS official Microsoft file with wrong SHA-256 rejected; it was never executed'
Write-Output "Evidence: $qaRoot"
