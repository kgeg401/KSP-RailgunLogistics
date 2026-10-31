param(
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [Parameter(Mandatory=$true)][string]$KspSource,
    [Parameter(Mandatory=$true)][string]$ServerSource
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
if(![IO.Path]::IsPathRooted($OutputRoot) -or (Test-Path -LiteralPath $OutputRoot)) { throw 'Use a new absolute private fixture folder.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$ok,[string]$label) { if(!$ok){throw $label};$checks.Add($label) }
foreach($mode in @('shell','legacy')) {
    $base=Join-Path $OutputRoot $mode
    $package=Join-Path $base 'package';$game=Join-Path $base 'game';$server=Join-Path $base 'server'
    foreach($folder in @('package/GameData/RailgunLogistics/Parts','package/GameData/RailgunLogistics/Plugins','package/Server/Plugins','game/GameData/RailgunLogistics','game/GameData/LunaMultiplayer/Plugins','game/GameData/MechJeb2/Plugins','game/GameData/000_Harmony','server/Config')) {
        New-Item -ItemType Directory -Path (Join-Path $base $folder) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $repo 'Install.ps1') -Destination $package
    Copy-Item -LiteralPath (Join-Path $repo 'src/Client/bin/Release/net472/RailgunLogistics.dll') -Destination (Join-Path $package 'GameData/RailgunLogistics/Plugins')
    Copy-Item -LiteralPath (Join-Path $repo 'src/Server/bin/Release/net6.0/RailgunLogistics.Server.dll') -Destination (Join-Path $package 'Server/Plugins')
    $parts=[IO.File]::ReadAllText((Join-Path $repo 'sandbox/GameData/RailgunSandbox/ReusableParts.cfg'))
    if($mode -eq 'legacy') {$parts=$parts.Replace('@name = krlLaunchShell','@name = legacyFixtureShell')}
    [IO.File]::WriteAllText((Join-Path $package 'GameData/RailgunLogistics/Parts/ReusableParts.cfg'),$parts)
    foreach($path in @('GameData/LunaMultiplayer/Plugins/LmpClient.dll','GameData/MechJeb2/Plugins/MechJeb2.dll','GameData/000_Harmony/0Harmony.dll')) {
        Copy-Item -LiteralPath (Join-Path $KspSource $path) -Destination (Join-Path $game $path)
    }
    [IO.File]::WriteAllText((Join-Path $game 'KSP_x64.exe'),'Inert installer fixture; never execute')
    [IO.File]::WriteAllText((Join-Path $game 'buildID64.txt'),'build id = 03190')
    [IO.File]::WriteAllText((Join-Path $game 'GameData/ModuleManager.fixture.dll'),'Presence fixture; never execute')
    $foreign=Join-Path $game 'GameData/RailgunLogistics/keep-me.txt'
    [IO.File]::WriteAllText($foreign,'Existing user file preserved exactly')
    $before=(Get-FileHash -LiteralPath $foreign -Algorithm SHA256).Hash
    Copy-Item -LiteralPath (Join-Path $ServerSource 'Server.dll') -Destination $server
    [xml]$control=Get-Content -LiteralPath (Join-Path $ServerSource 'Config/LMPModControl.xml') -Raw
    foreach($node in @($control.SelectNodes("//AllowedParts/string[text()='krlLaunchShell']"))) {[void]$node.ParentNode.RemoveChild($node)}
    $control.Save((Join-Path $server 'Config/LMPModControl.xml'))
    1..2 | ForEach-Object {
        & (Join-Path $package 'Install.ps1') -KspPath $game -ServerPath $server
        [xml]$after=Get-Content -LiteralPath (Join-Path $server 'Config/LMPModControl.xml') -Raw
        Check (@($after.SelectNodes("//AllowedParts/string[text()='krlLaunchShell']")).Count -eq $(if($mode -eq 'shell'){1}else{0})) "$mode install $_ admits only packaged shell, without duplicates"
        Check ((Get-FileHash -LiteralPath $foreign -Algorithm SHA256).Hash -eq $before) "$mode install $_ preserves unrelated user file"
        $required=$after.SelectSingleNode("//MandatoryPlugins/DllFile[FilePath='RailgunLogistics/Plugins/RailgunLogistics.dll']/Sha")
        Check ($required.InnerText.Replace('-','') -eq (Get-FileHash -LiteralPath (Join-Path $package 'GameData/RailgunLogistics/Plugins/RailgunLogistics.dll') -Algorithm SHA256).Hash) "$mode install $_ pins the actual candidate client"
        Start-Sleep -Milliseconds 1100
    }
}
[pscustomobject]@{Passed=$true;Checks=$checks;Scope='Real installer in inert disposable folders; no native flight or production installation';Evidence=$OutputRoot} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'report.json') -Encoding utf8
Write-Output "PASS $($checks.Count) installer checks; no fixture executable was started."
