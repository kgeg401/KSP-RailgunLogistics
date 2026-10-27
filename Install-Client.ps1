param([Parameter(Mandatory=$true)][string]$KspPath)
$ErrorActionPreference='Stop'
$KspPath=(Resolve-Path -LiteralPath $KspPath).Path
if(!(Test-Path -LiteralPath (Join-Path $KspPath 'KSP_x64.exe'))) { throw 'Choose the KSP directory containing KSP_x64.exe.' }
if((Get-Content -LiteralPath (Join-Path $KspPath 'buildID64.txt') -Raw) -notmatch 'build id = 0?3190\b') { throw 'Requires KSP 1.12.5 build 3190.' }
$lmp=Join-Path $KspPath 'GameData\LunaMultiplayer\Plugins\LmpClient.dll'
if([Reflection.AssemblyName]::GetAssemblyName($lmp).Version.ToString() -ne '0.29.2.0') { throw 'Requires Luna Multiplayer 0.29.2.' }
if(!(Get-ChildItem -LiteralPath (Join-Path $KspPath 'GameData') -Filter 'ModuleManager*.dll')) { throw 'ModuleManager required.' }
$exe=Join-Path $KspPath 'KSP_x64.exe'
if(Get-CimInstance Win32_Process -Filter "Name='KSP_x64.exe'" | Where-Object { !$_.ExecutablePath -or $_.ExecutablePath -eq $exe }) { throw 'Close this KSP instance before installation.' }
$source=Join-Path $PSScriptRoot 'GameData\RailgunLogistics'
$target=Join-Path $KspPath 'GameData\RailgunLogistics'
$stamp=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$state=Join-Path $KspPath 'RailgunLogistics-Updates'
New-Item -ItemType Directory -Path $state -Force | Out-Null
$stage=Join-Path $state "initial-$stamp"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach($directory in @($KspPath,$target,$state)) {
    for($d=New-Object IO.DirectoryInfo($directory);$null -ne $d;$d=$d.Parent) {
        if($d.Exists -and ($d.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Install directory cannot contain junctions or symbolic links.' }
    }
}
if(Test-Path -LiteralPath $target) {
    if(Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Linked mod files refused.' }
    Copy-Item -Path (Join-Path $target '*') -Destination $stage -Recurse -Force
}
Copy-Item -Path (Join-Path $source '*') -Destination $stage -Recurse -Force
if(Test-Path -LiteralPath (Join-Path $target 'Settings.xml')) { Copy-Item -LiteralPath (Join-Path $target 'Settings.xml') -Destination (Join-Path $stage 'Settings.xml') -Force }
[xml]$manifest=Get-Content -LiteralPath (Join-Path $stage 'InstalledRelease.xml') -Raw
foreach($file in $manifest.Release.File) {
    if((Get-FileHash -LiteralPath (Join-Path $stage $file.Path) -Algorithm SHA256).Hash -ne $file.Sha256) { throw 'Package checksum failed.' }
}
$backup=Join-Path $KspPath "RailgunLogistics-Backups\setup-$stamp"
New-Item -ItemType Directory -Path (Split-Path $backup) -Force | Out-Null
if(Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination $backup }
try { Move-Item -LiteralPath $stage -Destination $target } catch { if(Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $target }; throw }
Write-Output "Installed client with startup updates. Backup: $backup. The server and saves were not modified."
