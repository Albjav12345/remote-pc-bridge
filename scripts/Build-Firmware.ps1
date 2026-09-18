$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$localCli = Join-Path $projectRoot '.tools\arduino-cli\arduino-cli.exe'
$localConfig = Join-Path $projectRoot '.tools\arduino.yaml'
$globalCli = Get-Command arduino-cli -ErrorAction SilentlyContinue
if ($globalCli) {
    $cli = $globalCli.Source
    $common = @()
} elseif (Test-Path -LiteralPath $localCli) {
    $cli = $localCli
    $common = if (Test-Path -LiteralPath $localConfig) { @('--config-file', $localConfig) } else { @() }
} else {
    throw 'Falta Arduino CLI. Instala arduino-cli, el core esp32:esp32 3.3.2 y ArduinoJson 7.4.2.'
}
$sketch = Join-Path $projectRoot 'firmware\TorreBridge'
$build = Join-Path $projectRoot '.build\release-generic'
$release = Join-Path $projectRoot 'firmware\release'
New-Item -ItemType Directory -Force -Path $build, $release | Out-Null
& $cli @common compile --fqbn esp32:esp32:esp32 --build-path $build $sketch
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el firmware genérico.' }
$merged = Join-Path $build 'TorreBridge.ino.merged.bin'
if (!(Test-Path -LiteralPath $merged)) { throw 'Arduino CLI no generó la imagen unificada.' }
$target = Join-Path $release 'RemotePcBridge-esp32-4mb.bin'
Copy-Item -LiteralPath $merged -Destination $target -Force
Get-FileHash -Algorithm SHA256 -LiteralPath $target
