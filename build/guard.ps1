# No-web-view / no-network guard (ADR-0002). Fails the build when product code could reach a web view or the network.
# Scans src/ only (tests and build tooling may download fixtures in CI, never the app).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$patterns = @(
  'WebView2', 'CoreWebView2', 'Microsoft\.Web\.WebView', 'System\.Windows\.Controls\.WebBrowser', '<WebBrowser',
  'HttpClient', 'WebClient', 'HttpWebRequest', 'System\.Net\.Sockets', 'TcpClient', 'UdpClient', 'WebSocket',
  'System\.Net\.Http', 'HttpListener', 'Kestrel', 'AspNetCore', 'ApplicationInsights', 'Telemetry'
)
$files = Get-ChildItem -Path (Join-Path $root 'src') -Recurse -File -Include *.cs,*.xaml,*.csproj,*.props |
  Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
$hits = foreach ($f in $files) {
  foreach ($p in $patterns) {
    Select-String -Path $f.FullName -Pattern $p -AllMatches | ForEach-Object { "$($f.FullName):$($_.LineNumber): $p" }
  }
}
$pkgs = Get-Content (Join-Path $root 'Directory.Packages.props') -Raw
if ($pkgs -match 'WebView2') { $hits += 'Directory.Packages.props: WebView2 package' }
if ($hits) { $hits | ForEach-Object { Write-Host "GUARD: $_" }; Write-Error "guard failed: $($hits.Count) forbidden reference(s)"; exit 1 }
Write-Host "guard ok: $($files.Count) files scanned, 0 forbidden references"
