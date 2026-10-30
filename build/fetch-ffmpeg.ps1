# Build/CI provisioning only. The application never downloads anything.
# Pinned LGPL (no GPL parts, no libx264/x265/fdk-aac) static Windows x64 FFmpeg n8.1.3 from BtbN/FFmpeg-Builds.
# Licence: LGPL v2.1+. Used only as a separate executable (subprocess), never linked, so the app's code is unaffected;
# ship the licence text and the source offer (https://ffmpeg.org/legal.html) with the installer.
param([string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) 'third_party/ffmpeg'))
$ErrorActionPreference = 'Stop'
$name = 'ffmpeg-n8.1.3-25-g611cec792a-win64-lgpl-8.1.zip'
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-10-29-19-17/$name"
$sha256 = '74e7863feef0d40b72236caf53823b132bcd429657be742d0a9213c0581cb573'
$bin = Join-Path $Destination 'bin'
$ffmpeg = Join-Path $bin 'ffmpeg.exe'
$stamp = Join-Path $Destination "$sha256.ok"
if ((Test-Path -LiteralPath $ffmpeg) -and (Test-Path -LiteralPath $stamp)) { Write-Host "FFmpeg already provisioned ($name)"; return }
[void](New-Item -ItemType Directory -Force -Path $Destination)
$zip = Join-Path $Destination ('.ffmpeg-' + [guid]::NewGuid().ToString('N') + '.zip')
$extract = Join-Path $Destination ('.extract-' + [guid]::NewGuid().ToString('N'))
try {
    Invoke-WebRequest -Uri $url -OutFile $zip
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha256) { throw 'FFmpeg SHA-256 mismatch; download discarded.' }
    Expand-Archive -LiteralPath $zip -DestinationPath $extract
    $root = Get-ChildItem -LiteralPath $extract -Directory | Select-Object -First 1
    if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
    Move-Item -LiteralPath (Join-Path $root.FullName 'bin') -Destination $bin
    foreach ($doc in 'LICENSE.txt') { if (Test-Path (Join-Path $root.FullName $doc)) { Copy-Item (Join-Path $root.FullName $doc) (Join-Path $Destination 'FFMPEG-LICENSE.txt') -Force } }
    & $ffmpeg -hide_banner -version | Select-Object -First 1
    $conf = (& $ffmpeg -hide_banner -buildconf) -join ' '
    if ($conf -match '--enable-gpl' -or $conf -match '--enable-nonfree') { throw 'This FFmpeg build is not LGPL-only.' }
    Set-Content -LiteralPath $stamp -Value $name
    Write-Host "FFmpeg provisioned: $name"
} finally {
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
}
