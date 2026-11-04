# -Only runs CEF start-up and one self-test step (settings, shell, document, presentation, form-download, layout-load,
# downloads, office, zoom, theme, notices, live, logout) for a quick check while working on it; a release run uses every step.
param([ValidateSet('x64','x86')][string]$Platform = 'x64', [ValidateSet('Modern','Legacy')][string]$BrowserFlavor = 'Modern', [switch]$Live, [string]$OutputRoot,
    [ValidateSet('settings','shell','document','presentation','form-download','layout-load','downloads','office','zoom','theme','notices','live','logout')][string]$Only)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$framework = if ($BrowserFlavor -eq 'Legacy') { 'net452' } else { 'net462' }
$output = if ($OutputRoot) { Join-Path ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputRoot)) "$($BrowserFlavor.ToLowerInvariant())\$Platform" } else { Join-Path $projectRoot "src\Univer.Desktop\bin\$BrowserFlavor\$Platform\Release\$framework" }
$app = Join-Path $output 'Univer.Desktop.exe'
if (!(Test-Path -LiteralPath $app)) { throw 'Build the application first.' }
$qaMutex = New-Object Threading.Mutex($false, "Local\Univer.Desktop.Smoke.$BrowserFlavor.$Platform")
$qaOwned = $false
try {
    try { $qaOwned = $qaMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $qaOwned = $true }
    if (!$qaOwned) { throw "Another $Platform smoke run is active." }
    $arguments = @('--self-test')
    # From an elevated shell Chromium 152 would relaunch the app without elevation and this process would exit
    # before the checks run. The isolated QA run keeps its own process instead.
    $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($elevated -and $BrowserFlavor -eq 'Modern') { $arguments += '--do-not-de-elevate' }
    if ($Live) { $arguments += '--live-smoke' }
    if ($Only) { $arguments += "--self-test-only=$Only" }
    # The office step opens the synthetic workbooks and presentations Excel and PowerPoint saved in both formats.
    $env:UNIVER_QA_FIXTURES = Join-Path $projectRoot 'tests\Univer.Desktop.Tests\Fixtures'
    $process = Start-Process -FilePath $app -ArgumentList $arguments -WindowStyle Hidden -PassThru
    # The native run includes real Chromium download, keyboard, Office preview, file zoom and theme checks (about 2 minutes);
    # 300 s leaves room on slow machines.
    $deadline = [DateTime]::UtcNow.AddSeconds(300)
    while (!$process.WaitForExit(1000) -and [DateTime]::UtcNow -lt $deadline) { }
    if (!$process.HasExited) {
        # This exact process was created above with an isolated QA profile; never target a user's running app.
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Smoke timed out; inspect self-test.trace in $output."
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Smoke failed (exit $($process.ExitCode)); inspect self-test.failed and self-test.trace in $output." }
    Get-Content -LiteralPath (Join-Path $output 'self-test.ok')
}
finally {
    if ($qaOwned) { $qaMutex.ReleaseMutex() }
    $qaMutex.Dispose()
}
