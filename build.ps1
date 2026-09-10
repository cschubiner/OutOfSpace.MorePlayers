param(
    [string]$GameDir = 'G:\SteamLibrary\steamapps\common\Out of Space',
    [string]$BepInExDir = (Join-Path $GameDir 'BepInEx')
)
$ErrorActionPreference = 'Stop'
dotnet build (Join-Path $PSScriptRoot 'MorePlayers.csproj') -c Release --nologo "-p:GameDir=$GameDir" "-p:BepInExDir=$BepInExDir" -o (Join-Path $PSScriptRoot 'release')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
