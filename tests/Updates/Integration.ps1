param([string]$BundleVersion='0.2.3', [string]$HelperPath='')
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$build=Join-Path $project "update-release\$BundleVersion"
$lab=Join-Path $project ('evidence\updater-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
$game=Join-Path $lab 'fake-game'
$mod=Join-Path $game 'GameData\RailgunLogistics'
$lmp=Join-Path $game 'GameData\LunaMultiplayer\Plugins'
New-Item -ItemType Directory -Path $mod,$lmp -Force | Out-Null
Copy-Item -LiteralPath $env:ComSpec -Destination (Join-Path $game 'KSP_x64.exe')
Copy-Item -LiteralPath 'C:\SteamLibrary\steamapps\common\Kerbal Space Program\GameData\LunaMultiplayer\Plugins\LmpClient.dll' -Destination $lmp
[xml]$releaseDescriptor=Get-Content -LiteralPath (Join-Path $build 'friends.xml') -Raw
if ($releaseDescriptor.Release.ReusableFleet -eq 'true') {
    foreach($dependency in @('MechJeb2\Plugins\MechJeb2.dll','000_Harmony\0Harmony.dll')) {
        $target=Join-Path $game "GameData\$dependency"; New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path 'C:\SteamLibrary\steamapps\common\Kerbal Space Program\GameData' $dependency) -Destination $target
    }
    Set-Content -LiteralPath (Join-Path $game 'GameData\ModuleManager.4.2.3.dll') -Value 'presence fixture'
}
Set-Content -LiteralPath (Join-Path $game 'buildID64.txt') -Value 'build id = 03190'
Set-Content -LiteralPath (Join-Path $mod 'Settings.xml') -Value 'user settings preserved exactly' -Encoding Unicode
Set-Content -LiteralPath (Join-Path $mod 'local-note.txt') -Value 'keep extra files'
Set-Content -LiteralPath (Join-Path $game 'persistent-sentinel.sfs') -Value 'save progress'
$settingsHash=(Get-FileHash -LiteralPath (Join-Path $mod 'Settings.xml')).Hash
$helper=Join-Path $build 'package\GameData\RailgunLogistics\Updater\ApplyUpdate.exe'
if ($HelperPath) { $helper=(Resolve-Path -LiteralPath $HelperPath).Path }
$job=Join-Path $lab 'job-good'; New-Item -ItemType Directory -Path $job | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'friends.xml') -Destination (Join-Path $job 'release.xml')
Copy-Item -LiteralPath (Join-Path $build "RailgunLogistics-Update-$BundleVersion.zip") -Destination (Join-Path $job 'client.zip')
$fake=Start-Process -FilePath (Join-Path $game 'KSP_x64.exe') -ArgumentList '/c ping.exe -n 31 127.0.0.1 >nul' -WindowStyle Hidden -PassThru
$apply=Start-Process -FilePath $helper -ArgumentList @(('"'+$game+'"'),('"'+$job+'"')) -WindowStyle Hidden -PassThru
$waitingResult=Join-Path $job 'result.txt'
$startupDeadline=[DateTime]::UtcNow.AddSeconds(15)
while(!(Test-Path -LiteralPath $waitingResult) -and !$apply.HasExited -and [DateTime]::UtcNow -lt $startupDeadline) { Start-Sleep -Milliseconds 100 }
if($fake.HasExited) { throw 'The synthetic game exited before the helper waiting check.' }
if(Test-Path -LiteralPath (Join-Path $mod 'InstalledRelease.xml')) { throw 'Updated while matching game process was running.' }
if(!(Test-Path -LiteralPath $waitingResult) -or (Get-Content -LiteralPath $waitingResult -Raw) -notmatch 'Waiting') { throw 'Helper did not wait for the game.' }
$apply.WaitForExit(45000) | Out-Null
if(!$apply.HasExited -or $apply.ExitCode -ne 0) { throw 'Helper failed to apply after game exit.' }
if((Get-FileHash -LiteralPath (Join-Path $mod 'Settings.xml')).Hash -ne $settingsHash) { throw 'Local settings changed.' }
if((Get-Content -LiteralPath (Join-Path $mod 'local-note.txt') -Raw).Trim() -ne 'keep extra files') { throw 'Extra files lost.' }
if((Get-Content -LiteralPath (Join-Path $game 'persistent-sentinel.sfs') -Raw).Trim() -ne 'save progress') { throw 'Unrelated save changed.' }
$backup=Get-ChildItem -LiteralPath (Join-Path $game 'RailgunLogistics-Backups') -Directory | Select-Object -First 1
if((Get-FileHash -LiteralPath (Join-Path $backup.FullName 'Settings.xml')).Hash -ne $settingsHash) { throw 'Backup missing original settings.' }
$before=(Get-FileHash -LiteralPath (Join-Path $mod 'Plugins\RailgunLogistics.dll')).Hash
$jobBad=Join-Path $lab 'job-corrupt'; New-Item -ItemType Directory -Path $jobBad | Out-Null
Copy-Item -LiteralPath (Join-Path $job 'release.xml') -Destination $jobBad
Set-Content -LiteralPath (Join-Path $jobBad 'client.zip') -Value 'broken'
$bad=Start-Process -FilePath $helper -ArgumentList @(('"'+$game+'"'),('"'+$jobBad+'"')) -WindowStyle Hidden -PassThru -Wait
if($bad.ExitCode -ne 1) { throw 'Corrupt package was not rejected.' }
if((Get-FileHash -LiteralPath (Join-Path $mod 'Plugins\RailgunLogistics.dll')).Hash -ne $before) { throw 'Corrupt update changed installed DLL.' }
if(Test-Path -LiteralPath (Join-Path $build 'package\Install-Client.ps1')) { throw 'An installer was included in the drop-in package.' }
Write-Output "PASS: waits for exact game, applies on exit, verifies bytes, preserves settings/extras/save sentinel, backs up, rejects corrupt download; drop-in package contains no installer. Evidence: $lab"
