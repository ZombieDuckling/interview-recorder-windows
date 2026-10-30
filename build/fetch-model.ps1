# Build/provisioning only. The application never downloads weights.
param([string]$Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) 'models'))
$ErrorActionPreference = 'Stop'
$model = (Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'models/manifest.json') -Raw | ConvertFrom-Json).models[0]
[void](New-Item -ItemType Directory -Force -Path $Destination)
$target = Join-Path $Destination $model.file
function Test-Model([string]$Path) {
    return ((Test-Path -LiteralPath $Path -PathType Leaf) -and
        (Get-Item -LiteralPath $Path).Length -eq $model.size -and
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $model.sha256)
}
if (Test-Model $target) { Write-Host "Model already verified: $($model.name)"; return }
$temp = Join-Path $Destination ('.model-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    Invoke-WebRequest -Uri $model.sourceUrl -OutFile $temp
    if (-not (Test-Model $temp)) { throw 'Model size or SHA-256 mismatch; download discarded.' }
    $stream = [System.IO.File]::Open($temp, 'Open', 'ReadWrite', 'None')
    try { $stream.Flush($true) } finally { $stream.Dispose() }
    [System.IO.File]::Move($temp, $target, $true)
    Write-Host "Model verified: $($model.name)"
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
}
