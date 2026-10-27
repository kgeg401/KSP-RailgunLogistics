param(
    [Parameter(Mandatory=$true)][string]$KspPath,
    [string]$ServerPath
)
$ErrorActionPreference = 'Stop'
$packageRoot = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'release\GameData')) { Join-Path $PSScriptRoot 'release' } else { $PSScriptRoot }
$gameExecutable = Join-Path $KspPath 'KSP_x64.exe'
if (!(Test-Path -LiteralPath $gameExecutable)) { throw 'Select the real KSP folder containing KSP_x64.exe.' }
$buildText = Get-Content -LiteralPath (Join-Path $KspPath 'buildID64.txt') -Raw
if ($buildText -notmatch 'build id = 0?3190') { throw 'This build requires KSP 1.12.5 / build 3190.' }
if (!(Test-Path -LiteralPath (Join-Path $KspPath 'GameData\LunaMultiplayer\Plugins\LmpClient.dll'))) { throw 'Install matching Luna Multiplayer 0.29.2 first.' }
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $KspPath 'GameData\LunaMultiplayer\Plugins\LmpClient.dll')).Version.ToString() -ne '0.29.2.0') { throw 'This release targets Luna Multiplayer 0.29.2.' }
if (!(Get-ChildItem -LiteralPath (Join-Path $KspPath 'GameData') -Filter 'ModuleManager*.dll')) { throw 'ModuleManager is required for existing-ship receiver menus.' }
if ($ServerPath -and (!(Test-Path -LiteralPath (Join-Path $ServerPath 'Server.dll')) -or !(Test-Path -LiteralPath (Join-Path $ServerPath 'Config\LMPModControl.xml')))) { throw 'Select a valid Luna server folder.' }
if ($ServerPath -and [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ServerPath 'Server.dll')).Version.ToString() -ne '0.29.2.0') { throw 'This release targets Luna server 0.29.2.' }
$active = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $gameExecutable -or ($ServerPath -and $_.ExecutablePath -eq (Join-Path $ServerPath 'Server.exe')) }
if ($active) { throw 'Close this KSP instance and its Luna server before installation. No processes were stopped.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $KspPath "RailgunLogistics-Backups\$stamp"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$clientTarget = Join-Path $KspPath 'GameData\RailgunLogistics'
if (Test-Path -LiteralPath $clientTarget) { Copy-Item -LiteralPath $clientTarget -Destination (Join-Path $backupRoot 'RailgunLogistics') -Recurse }
Copy-Item -LiteralPath (Join-Path $packageRoot 'GameData\RailgunLogistics') -Destination (Join-Path $KspPath 'GameData') -Recurse -Force
if ($ServerPath) {
    if (!(Test-Path -LiteralPath (Join-Path $ServerPath 'Server.dll'))) { throw 'Select a valid Luna server folder.' }
    $pluginTarget = Join-Path $ServerPath 'Plugins\RailgunLogistics.Server.dll'
    if (Test-Path -LiteralPath $pluginTarget) { Copy-Item -LiteralPath $pluginTarget -Destination $backupRoot }
    $controlPath = Join-Path $ServerPath 'Config\LMPModControl.xml'
    Copy-Item -LiteralPath $controlPath -Destination $backupRoot
    New-Item -ItemType Directory -Path (Join-Path $ServerPath 'Plugins') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $packageRoot 'Server\Plugins\RailgunLogistics.Server.dll') -Destination $pluginTarget -Force
    [xml]$control = Get-Content -LiteralPath $controlPath -Raw
    $root = $control.DocumentElement
    foreach($partName in @('kgeg4KerbinRailgun','kgeg4RailgunCargoPod')) {
        if (!$root.AllowedParts.SelectSingleNode("string[text()='$partName']")) {
            $part = $control.CreateElement('string'); $part.InnerText = $partName; $root.AllowedParts.AppendChild($part) | Out-Null
        }
    }
    $mandatory = $root.SelectSingleNode('MandatoryPlugins')
    $relativeDll = 'RailgunLogistics/Plugins/RailgunLogistics.dll'
    $entry = $mandatory.SelectSingleNode("DllFile[FilePath='$relativeDll']")
    if (!$entry) { $entry=$control.CreateElement('DllFile'); $mandatory.AppendChild($entry) | Out-Null }
    $hash = (Get-FileHash -LiteralPath (Join-Path $clientTarget 'Plugins\RailgunLogistics.dll') -Algorithm SHA256).Hash
    $dashedHash = ($hash -split '(.{2})' | Where-Object { $_ }) -join '-'
    foreach ($pair in @{FilePath=$relativeDll;Text='Railgun Logistics 0.2.0 physical docking pods - install the same client package before joining.';Sha=$dashedHash;Link=''}.GetEnumerator()) {
        $field = $entry.SelectSingleNode($pair.Key)
        if (!$field) { $field = $control.CreateElement($pair.Key); $entry.AppendChild($field) | Out-Null }
        $field.InnerText = $pair.Value
    }
    $control.Save($controlPath)
}
Write-Output "Railgun Logistics installed. Backup: $backupRoot. The game and server were not started. Every player needs the client package."
