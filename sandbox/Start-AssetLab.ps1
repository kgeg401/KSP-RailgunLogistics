param(
    [Parameter(Mandatory=$true)][string]$LabRoot,
    [Parameter(Mandatory=$true)][string]$ClientDll,
    [Parameter(Mandatory=$true)][string]$ReceiptPath,
    [switch]$Visible,
    [ValidateSet('prepare','snapshot','rail-payload','rail-audit')][string]$InitialCommand='snapshot'
)
$ErrorActionPreference='Stop'
$resolvedLab=(Resolve-Path -LiteralPath $LabRoot).Path.TrimEnd('\')
$source=(Resolve-Path -LiteralPath $ClientDll).Path
$markerPath=Join-Path $resolvedLab 'RailgunAssetLab.json'
if(!(Test-Path -LiteralPath $markerPath)){throw 'This is not a marked private asset lab.'}
$marker=Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
if($marker.purpose -ne 'railgun-private-assets' -or $marker.root -ne $resolvedLab){throw 'The private lab marker does not match this folder.'}
$existing=@(Get-Process KSP_x64 -ErrorAction SilentlyContinue)
if($existing.Count -ne 0){throw 'KSP is already open. Close it before installing a private test build.'}
$executable=Join-Path $resolvedLab 'KSP_x64.exe'
$save=Join-Path $resolvedLab 'saves\Railgun-Shell-Test'
if(!(Test-Path -LiteralPath $executable) -or !(Test-Path -LiteralPath (Join-Path $save 'persistent.sfs')) -or !(Test-Path -LiteralPath (Join-Path $save 'RailgunReusableFleet.xml'))){throw 'The isolated game or its paired native save is missing.'}
$destination=Join-Path $resolvedLab 'GameData\RailgunLogistics\Plugins\RailgunLogistics.dll'
$sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
Copy-Item -LiteralPath $source -Destination $destination -Force
$installedHash=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if($sourceHash -ne $installedHash){throw 'The installed client does not match the requested build.'}
$command=Join-Path $save 'RailgunSandbox.command'
if(Test-Path -LiteralPath $command){throw 'An earlier test command is still pending.'}
Set-Content -LiteralPath $command -Value $InitialCommand -Encoding ascii
if(@(Get-Process KSP_x64 -ErrorAction SilentlyContinue).Count -ne 0){throw 'Another KSP instance opened during installation; no second instance was started.'}
$windowStyle=if($Visible){'Normal'}else{'Hidden'}
$process=Start-Process -FilePath $executable -WorkingDirectory $resolvedLab -ArgumentList '-railgun-shell','-screen-fullscreen','0','-screen-width','1600','-screen-height','900' -WindowStyle $windowStyle -PassThru
$receipt=[pscustomobject]@{Id=$process.Id;Started=[DateTime]::UtcNow;Root=$resolvedLab;SourceSHA256=$sourceHash;InstalledSHA256=$installedHash;InitialCommand=$InitialCommand}
$receipt | ConvertTo-Json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8
$receipt | ConvertTo-Json
