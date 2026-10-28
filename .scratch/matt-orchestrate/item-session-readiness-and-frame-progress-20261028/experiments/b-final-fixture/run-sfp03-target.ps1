param(
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][ValidateSet('RED', 'GREEN')][string]$CaseName
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepoRoot)
$pluginPath = Join-Path $root 'bin/Release/SteamP2PFriends.dll'
$testPluginPath = Join-Path $root 'WhitelistTests/bin/Release/SteamP2PFriends.dll'
$testPath = Join-Path $root 'WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe'
foreach ($path in @($pluginPath, $testPluginPath, $testPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "missing isolated input: $path" }
}
$cecilPath = Join-Path (Split-Path $root -Parent) 'Libs/Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $cecilPath -PathType Leaf)) { throw "missing declared metadata reader: $cecilPath" }
Add-Type -Path $cecilPath
$reader = [Mono.Cecil.ReaderParameters]::new()
$productionMetadata = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pluginPath, $reader)
$testPluginMetadata = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($testPluginPath, $reader)
$testAssemblyMetadata = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($testPath, $reader)
$productionMvid = $productionMetadata.MainModule.Mvid
$testPluginMvid = $testPluginMetadata.MainModule.Mvid
$testAssemblyMvid = $testAssemblyMetadata.MainModule.Mvid
$productionMetadata.Dispose()
$testPluginMetadata.Dispose()
$testAssemblyMetadata.Dispose()
if ($productionMvid -ne $testPluginMvid) { throw 'production and physical test-copy MVID differ' }
$testPlugin = [Reflection.Assembly]::LoadFrom($testPluginPath)
if (-not [string]::Equals([IO.Path]::GetFullPath($testPlugin.Location), $testPluginPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "loaded plugin did not come from the physical test copy: $($testPlugin.Location)"
}
$testAssembly = [Reflection.Assembly]::LoadFrom($testPath)
if ((-not [string]::Equals([IO.Path]::GetFullPath($testAssembly.Location), $testPath, [StringComparison]::OrdinalIgnoreCase)) -or ($testAssembly.ManifestModule.ModuleVersionId -ne $testAssemblyMvid)) {
    throw "loaded test assembly differs from physical isolated input: $($testAssembly.Location)"
}
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$program = $testAssembly.GetType('SteamP2PFriends.WhitelistTests.Program', $true)
$prelude = $program.GetMethod('InstallBattlEyeTypeResolutionStub', $flags)
if ($null -eq $prelude) { throw 'Framework runner prelude method is missing' }
$targetType = $testAssembly.GetType('SteamP2PFriends.WhitelistTests.ItemPreparationAndFirstDemandTests', $true)
$target = $targetType.GetMethod('Test_SFP03_RealFrameGateProgressesBoundedSettlement', $flags)
if ($null -eq $target) { throw 'SFP03 target method is missing' }
$rootPrefix = $root.TrimEnd('\') + '\'
foreach ($assembly in @($testPlugin, $testAssembly)) {
    if (-not [IO.Path]::GetFullPath($assembly.Location).StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "assembly escaped isolated case root: $($assembly.Location)"
    }
}
$loadedPlugins = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SteamP2PFriends' })
if ($loadedPlugins.Count -ne 1) { throw "expected one loaded SteamP2PFriends assembly, found $($loadedPlugins.Count)" }
$loadedPlugin = $loadedPlugins[0]
if ((-not [string]::Equals([IO.Path]::GetFullPath($loadedPlugin.Location), $testPluginPath, [StringComparison]::OrdinalIgnoreCase)) -or ($loadedPlugin.ManifestModule.ModuleVersionId -ne $testPluginMvid)) {
    throw "loaded SteamP2PFriends identity differs from isolated test copy: $($loadedPlugin.Location)"
}
$coordinator = $loadedPlugin.GetType('SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator', $true)
if ($coordinator.Assembly.ManifestModule.ModuleVersionId -ne $testPluginMvid) {
    throw 'coordinator resolved from a different SteamP2PFriends module'
}
Write-Output "case=$CaseName utc=$([DateTime]::UtcNow.ToString('o')) repo=$root"
Write-Output "productionPhysical=$pluginPath mvid=$productionMvid"
Write-Output "testPluginPhysical=$testPluginPath mvid=$testPluginMvid"
Write-Output "loadedPlugin=$($loadedPlugin.Location) mvid=$($loadedPlugin.ManifestModule.ModuleVersionId)"
Write-Output "tests=$($testAssembly.Location) mvid=$($testAssembly.ManifestModule.ModuleVersionId)"
try {
    $prelude.Invoke($null, @())
    $result = $target.Invoke($null, @())
    $loadedAfter = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SteamP2PFriends' })
    if (($loadedAfter.Count -ne 1) -or ($loadedAfter[0].ManifestModule.ModuleVersionId -ne $testPluginMvid) -or (-not [string]::Equals([IO.Path]::GetFullPath($loadedAfter[0].Location), $testPluginPath, [StringComparison]::OrdinalIgnoreCase))) {
        throw 'target did not execute with exactly the isolated test-copy plugin identity'
    }
    $loadedTestsAfter = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq $testAssembly.GetName().Name })
    if (($loadedTestsAfter.Count -ne 1) -or ($loadedTestsAfter[0].ManifestModule.ModuleVersionId -ne $testAssemblyMvid) -or (-not [string]::Equals([IO.Path]::GetFullPath($loadedTestsAfter[0].Location), $testPath, [StringComparison]::OrdinalIgnoreCase))) {
        throw 'target did not execute from exactly the isolated test assembly identity'
    }
    Write-Output "target=$($target.Name) result=$result"
    if ($result -ne $true) { exit 1 }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.ToString())
    exit 1
}
