param([string]$BundleVersion='0.2.3', [string]$GameplayClientDll='', [switch]$ReusableFleet, [string]$SharedUpdaterPath='')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
foreach($project in @('src\Updater\RailgunLogistics.Updater.csproj','src\UpdateHelper\ApplyUpdate.csproj') | Where-Object { !$SharedUpdaterPath }) {
    & dotnet build (Join-Path $root $project) -c Release --nologo
    if($LASTEXITCODE -ne 0) { throw "Failed: $project" }
}
$out=Join-Path $root "update-release\$BundleVersion"
if(Test-Path -LiteralPath $out) { throw 'Use a fresh bundle version/output directory; releases are not overwritten.' }
$bundle=Join-Path $out 'package'
$client=Join-Path $bundle 'GameData\RailgunLogistics'
New-Item -ItemType Directory -Path $client -Force | Out-Null
# Reuse the native-tested 0.2.0 payload byte-for-byte; this release only adds the updater.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$old=[IO.Compression.ZipFile]::OpenRead((Join-Path $root 'RailgunLogistics-0.2.0.zip'))
try {
    foreach($entry in $old.Entries) {
        $name=$entry.FullName.Replace('\','/')
        if($name.StartsWith('GameData/RailgunLogistics/')) {
            $dest=Join-Path $bundle $name
            New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$dest,$false)
        }
    }
} finally { $old.Dispose() }
if ($GameplayClientDll) {
    $gameplay = (Resolve-Path -LiteralPath $GameplayClientDll).Path
    if ([Reflection.AssemblyName]::GetAssemblyName($gameplay).Name -ne 'RailgunLogistics') { throw 'Expected the built RailgunLogistics client assembly.' }
    Copy-Item -LiteralPath $gameplay -Destination (Join-Path $client 'Plugins\RailgunLogistics.dll') -Force
}
if ($ReusableFleet) {
    if (!$GameplayClientDll) { throw 'A current integrated gameplay DLL is required for a reusable-fleet release.' }
    Copy-Item -LiteralPath (Join-Path $root 'sandbox\GameData\RailgunSandbox\ReusableParts.cfg') -Destination (Join-Path $client 'Parts\ReusableParts.cfg')
    $serverSource=Join-Path $root 'src\Server\bin\Release\net6.0\RailgunLogistics.Server.dll'
    if ([Reflection.AssemblyName]::GetAssemblyName($serverSource).Version.ToString(3) -ne $BundleVersion) { throw 'Build the matching shared-fleet server first.' }
    $serverTarget=Join-Path $bundle 'Server\Plugins'; New-Item -ItemType Directory -Path $serverTarget -Force | Out-Null
    Copy-Item -LiteralPath $serverSource -Destination $serverTarget
    Copy-Item -LiteralPath (Join-Path $root 'Install.ps1') -Destination $bundle
}
$updater=Join-Path $client 'Updater'
New-Item -ItemType Directory -Path $updater -Force | Out-Null
$updaterDll=Join-Path $root 'src\Updater\bin\Release\net472\RailgunLogistics.Updater.dll'
$helperExe=Join-Path $root 'src\UpdateHelper\bin\Release\net472\ApplyUpdate.exe'
if ($SharedUpdaterPath) {
    $shared=(Resolve-Path -LiteralPath $SharedUpdaterPath).Path
    $updaterDll=Join-Path $shared 'RailgunLogistics.Updater.dll'; $helperExe=Join-Path $shared 'ApplyUpdate.exe'
    if ([Reflection.AssemblyName]::GetAssemblyName($updaterDll).Name -ne 'RailgunLogistics.Updater' -or [Reflection.AssemblyName]::GetAssemblyName($helperExe).Name -ne 'ApplyUpdate') { throw 'Expected the common built updater/helper pair.' }
}
Copy-Item -LiteralPath $updaterDll -Destination $updater
Copy-Item -LiteralPath $helperExe -Destination $updater
$clientZip=Join-Path $out "RailgunLogistics-Update-$BundleVersion.zip"
$archive=[IO.Compression.ZipFile]::Open($clientZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($f in Get-ChildItem -LiteralPath $client -File -Recurse | Sort-Object FullName) {
        $relative=$f.FullName.Substring($client.Length+1).Replace('\','/')
        if($relative -eq 'Settings.xml') { continue } # local settings persist; only initial install supplies defaults
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$f.FullName,$relative) | Out-Null
    }
} finally { $archive.Dispose() }
$xml=New-Object Xml.XmlDocument
$release=$xml.CreateElement('Release'); $xml.AppendChild($release) | Out-Null
foreach($pair in @{ Version=$BundleVersion; KspBuild='3190'; LunaVersion='0.29.2.0'; PackageUrl="https://github.com/kgeg401/KSP-RailgunLogistics/releases/download/v$BundleVersion/RailgunLogistics-Update-$BundleVersion.zip"; Sha256=(Get-FileHash -LiteralPath $clientZip -Algorithm SHA256).Hash.ToLowerInvariant() }.GetEnumerator()) { $release.SetAttribute($pair.Key,$pair.Value) }
if ($ReusableFleet) { $release.SetAttribute('ReusableFleet','true') }
foreach($f in Get-ChildItem -LiteralPath $client -File -Recurse | Sort-Object FullName) {
    $relative=$f.FullName.Substring($client.Length+1).Replace('\','/')
    if($relative -eq 'Settings.xml') { continue }
    $node=$xml.CreateElement('File'); $node.SetAttribute('Path',$relative); $node.SetAttribute('Sha256',(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()); $release.AppendChild($node) | Out-Null
}
$xml.Save((Join-Path $out 'friends.xml'))
Copy-Item -LiteralPath (Join-Path $out 'friends.xml') -Destination (Join-Path $client 'InstalledRelease.xml')
foreach($f in @('README-Updates.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $root $f) -Destination $bundle }
[IO.Compression.ZipFile]::CreateFromDirectory($bundle,(Join-Path $out "RailgunLogistics-$BundleVersion.zip"))
Write-Output "Built $out. Not published or installed."
