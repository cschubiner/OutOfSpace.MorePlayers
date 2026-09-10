param([string]$GameDir = 'G:\SteamLibrary\steamapps\common\Out of Space')
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$exe = Join-Path $GameDir 'Out of Space.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Out of Space.exe was not found in GameDir.' }
if (Get-Process 'Out of Space' -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) { throw 'Close Out of Space before installing.' }
$assembly = Join-Path $GameDir 'Out of Space_Data\Managed\Assembly-CSharp.dll'
if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne '4D5E474A5AD7D6227DFB7CEF7D09FFA357A19CCF601D65503CFE01C68EAE934C') { throw 'This release targets Steam build 6616527 (v1.2.4b13). The installed game differs.' }
$sourceDll = Join-Path $PSScriptRoot 'release\OutOfSpace.MorePlayers.dll'
if (!(Test-Path -LiteralPath $sourceDll)) { throw 'Build the plugin first using build.ps1.' }
$coreDll = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
if (Test-Path -LiteralPath $coreDll) {
    if ([Reflection.AssemblyName]::GetAssemblyName($coreDll).Version.Major -ne 5) { throw 'An incompatible BepInEx major version is installed. This plugin requires BepInEx 5.' }
} else {
    if ((Test-Path -LiteralPath (Join-Path $GameDir 'winhttp.dll')) -or (Test-Path -LiteralPath (Join-Path $GameDir 'doorstop_config.ini'))) { throw 'An existing loader was found. Inspect it before installing BepInEx.' }
    $staging = Join-Path $PSScriptRoot '.installer'
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $zip = Join-Path $staging 'BepInEx_win_x64_5.4.23.5.zip'
    $url = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
    if (!(Test-Path -LiteralPath $zip)) { Invoke-WebRequest $url -OutFile $zip }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4') { throw 'BepInEx download hash mismatch.' }
    $unpack = Join-Path $staging 'loader'
    Expand-Archive -LiteralPath $zip -DestinationPath $unpack -Force
    $files = Get-ChildItem -LiteralPath $unpack -File -Recurse
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($unpack.Length).TrimStart('\')
        $target = Join-Path $GameDir $relative
        if (Test-Path -LiteralPath $target) { throw "Existing file would be overwritten: $target" }
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($unpack.Length).TrimStart('\')
        $target = Join-Path $GameDir $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
}
$plugins = Join-Path $GameDir 'BepInEx\plugins\MorePlayers'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
$targetDll = Join-Path $plugins 'OutOfSpace.MorePlayers.dll'
if (Test-Path -LiteralPath $targetDll) {
    Copy-Item -LiteralPath $targetDll -Destination ($targetDll + '.previous') -Force
}
Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
Write-Output "Installed More Local Players: $targetDll"
Write-Output 'Launch normally from Steam. Press F8 for diagnostics. Config is generated on first launch.'
