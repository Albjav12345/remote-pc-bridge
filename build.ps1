param([switch]$SkipFirmware)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$project = Join-Path $projectRoot 'src-modern\TorreRemota.csproj'
$output = Join-Path $projectRoot 'dist'
$firmware = Join-Path $projectRoot 'firmware\release\RemotePcBridge-esp32-4mb.bin'
$sources = @('firmware\TorreBridge\TorreBridge.ino', 'firmware\TorreBridge\roots.h') | ForEach-Object { Join-Path $projectRoot $_ }
$firmwareStale = !(Test-Path -LiteralPath $firmware)
if (!$firmwareStale) {
    $builtAt = (Get-Item -LiteralPath $firmware).LastWriteTimeUtc
    $firmwareStale = @($sources | Where-Object { (Get-Item -LiteralPath $_).LastWriteTimeUtc -gt $builtAt }).Count -gt 0
}
if (!$SkipFirmware -and $firmwareStale) {
    & (Join-Path $projectRoot 'scripts\Build-Firmware.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'La compilación del firmware ha fallado.' }
}
if (!(Test-Path -LiteralPath $firmware)) { throw 'Falta el firmware genérico para incrustar en la aplicación.' }
& dotnet publish $project -c Release -r win-x64 --self-contained false -o $output
if ($LASTEXITCODE -ne 0) { throw 'La compilación ha fallado.' }
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output 'TorreRemota.exe')
