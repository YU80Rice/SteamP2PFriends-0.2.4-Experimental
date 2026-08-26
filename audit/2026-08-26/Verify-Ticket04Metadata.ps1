$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runtimeRoot = Get-ChildItem 'C:\Program Files\dotnet\shared\Microsoft.NETCore.App' -Directory |
    Sort-Object Name -Descending | Select-Object -First 1
[System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath(
    (Join-Path $runtimeRoot.FullName 'System.Reflection.Metadata.dll')) | Out-Null
[System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromAssemblyPath(
    (Join-Path $runtimeRoot.FullName 'System.Collections.Immutable.dll')) | Out-Null

function Get-Mvid([string] $path) {
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromMetadataImage(
                $reader.GetMetadata().GetContent())
            try {
                $metadata = $provider.GetMetadataReader()
                return $metadata.GetGuid($metadata.GetModuleDefinition().Mvid).ToString()
            }
            finally { $provider.Dispose() }
        }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

$plugin = Join-Path $repoRoot 'bin\Release\SteamP2PFriends.dll'
$tests = Join-Path $repoRoot 'WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe'
foreach ($artifact in @($plugin, $tests)) {
    if (-not (Test-Path -LiteralPath $artifact)) { throw "Missing artifact: $artifact" }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($artifact)
    [pscustomobject]@{
        Path = $artifact
        SHA256 = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash
        MVID = Get-Mvid $artifact
        AssemblyVersion = ([Reflection.AssemblyName]::GetAssemblyName($artifact)).Version.ToString()
        FileVersion = $info.FileVersion
    }
}
