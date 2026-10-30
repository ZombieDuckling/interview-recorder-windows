# Authenticode signing hook (ADR-0004). Runs only when the repo has the SIGN_PFX_BASE64 / SIGN_PFX_PASSWORD secrets.
# No certificate exists yet: installers are unsigned until Josh provides one. Never commit or print the certificate.
param([Parameter(Mandatory)][string[]]$Files)
$ErrorActionPreference = 'Stop'
$pfx = Join-Path $env:RUNNER_TEMP 'sign.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGN_PFX_BASE64))
try {
  $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
  foreach ($f in $Files) {
    & $signtool.FullName sign /f $pfx /p $env:SIGN_PFX_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $f
    if ($LASTEXITCODE) { throw "signing failed: $f" }
    & $signtool.FullName verify /pa $f
    if ($LASTEXITCODE) { throw "verify failed: $f" }
  }
} finally { Remove-Item $pfx -Force }
