param([string]$GameDir = 'G:\SteamLibrary\steamapps\common\Out of Space')
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$exe = Join-Path $GameDir 'Out of Space.exe'
if (Get-Process 'Out of Space' -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) { throw 'Close the game before disabling the plugin.' }
$plugin = Join-Path $GameDir 'BepInEx\plugins\MorePlayers\OutOfSpace.MorePlayers.dll'
if (Test-Path -LiteralPath $plugin) { Move-Item -LiteralPath $plugin -Destination ($plugin + '.disabled') -Force }
Write-Output 'More Local Players disabled. Other mods and game files are unchanged.'
