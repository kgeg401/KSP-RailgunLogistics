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
$reusable=Test-Path -LiteralPath (Join-Path $packageRoot 'GameData\RailgunLogistics\Parts\ReusableParts.cfg')
if ($reusable) {
    foreach($dependency in @(@('MechJeb2\Plugins\MechJeb2.dll','2.14.3.0'),@('000_Harmony\0Harmony.dll','2.2.1.0'))) {
        $path=Join-Path $KspPath "GameData\$($dependency[0])"
        if (!(Test-Path -LiteralPath $path) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion -ne $dependency[1]) { throw "Install the shared server mod pack dependency $($dependency[0]) version $($dependency[1]) first." }
    }
    if (Test-Path -LiteralPath (Join-Path $KspPath 'GameData\RailgunSandbox\Plugins\RailgunSandbox.dll')) { throw 'Back up the older separate RailgunSandbox prototype outside GameData before installing the integrated version.' }
}
if ($ServerPath -and (!(Test-Path -LiteralPath (Join-Path $ServerPath 'Server.dll')) -or !(Test-Path -LiteralPath (Join-Path $ServerPath 'Config\LMPModControl.xml')))) { throw 'Select a valid Luna server folder.' }
if ($ServerPath -and [Reflection.AssemblyName]::GetAssemblyName((Join-Path $ServerPath 'Server.dll')).Version.ToString() -ne '0.29.2.0') { throw 'This release targets Luna server 0.29.2.' }
$active = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $gameExecutable -or ($ServerPath -and $_.ExecutablePath -eq (Join-Path $ServerPath 'Server.exe')) }
if ($active) { throw 'Close this KSP instance and its Luna server before installation. No processes were stopped.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $KspPath "RailgunLogistics-Backups\$stamp"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$clientTarget = Join-Path $KspPath 'GameData\RailgunLogistics'
if (Test-Path -LiteralPath $clientTarget) { Copy-Item -LiteralPath $clientTarget -Destination (Join-Path $backupRoot 'RailgunLogistics') -Recurse }
$clientSource=Join-Path $packageRoot 'GameData\RailgunLogistics'
foreach($file in Get-ChildItem -LiteralPath $clientSource -Recurse -File) {
    $relative=$file.FullName.Substring($clientSource.Length+1); $target=Join-Path $clientTarget $relative
    if ($relative -eq 'Settings.xml' -and (Test-Path -LiteralPath $target)) { continue }
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
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
    $parts=@('kgeg4KerbinRailgun','kgeg4RailgunCargoPod')
    if ($reusable) { $parts+=@('krlReusableProbe','krlReusableFuelCargo','krlReusableMonoCargo') }
    foreach($partName in $parts) {
        if (!$root.AllowedParts.SelectSingleNode("string[text()='$partName']")) {
            $part = $control.CreateElement('string'); $part.InnerText = $partName; $root.AllowedParts.AppendChild($part) | Out-Null
        }
    }
    $mandatory = $root.SelectSingleNode('MandatoryPlugins')
    $relativeDll = 'RailgunLogistics/Plugins/RailgunLogistics.dll'
    $mandatoryEntries=$mandatory.SelectNodes("DllFile[translate(FilePath,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='railgunlogistics/plugins/railgunlogistics.dll']")
    if($mandatoryEntries.Count -gt 1) { throw 'Duplicate mandatory Railgun Logistics entries need review; no server configuration was saved.' }
    $entry = $mandatoryEntries | Select-Object -First 1
    if (!$entry) { $entry=$control.CreateElement('DllFile'); $mandatory.AppendChild($entry) | Out-Null }
    $hash = (Get-FileHash -LiteralPath (Join-Path $clientTarget 'Plugins\RailgunLogistics.dll') -Algorithm SHA256).Hash
    $dashedHash = ($hash -split '(.{2})' | Where-Object { $_ }) -join '-'
    $gameplayVersion=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $clientTarget 'Plugins\RailgunLogistics.dll')).Version.ToString(3)
    $description=if($reusable){"Railgun Logistics $gameplayVersion shared reusable pods - finish the matching client update before joining."}else{'Railgun Logistics physical docking pods - install the same client package before joining.'}
    foreach ($pair in @{FilePath=$relativeDll;Text=$description;Sha=$dashedHash;Link=''}.GetEnumerator()) {
        $field = $entry.SelectSingleNode($pair.Key)
        if (!$field) { $field = $control.CreateElement($pair.Key); $entry.AppendChild($field) | Out-Null }
        $field.InnerText = $pair.Value
    }
    $control.Save($controlPath)
}
Write-Output "Railgun Logistics installed. Backup: $backupRoot. The game and server were not started. Every player needs the client package."
