$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'WhitelistTests\SteamP2PFriends.WhitelistTests.csproj'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$namespace = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
$namespace.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')
$compileIncludes = @($project.SelectNodes('//msb:Compile', $namespace) | ForEach-Object { $_.Include })

if (($compileIncludes | Where-Object { $_ -eq 'Program.cs' }).Count -ne 1) {
    throw 'Expected exactly one explicit Program.cs compile entry.'
}
if (($compileIncludes | Where-Object { $_ -eq 'Evidence\**\*.cs' }).Count -ne 1) {
    throw 'Expected exactly one Evidence wildcard compile entry.'
}

$evidenceRoot = Join-Path $repoRoot 'WhitelistTests\Evidence'
$classes = @('PureMemory', 'StaticIL', 'BuildArtifact', 'Runtime')
foreach ($class in $classes) {
    $classPath = Join-Path $evidenceRoot $class
    if (-not (Test-Path -LiteralPath $classPath -PathType Container)) {
        throw "Missing Evidence Class directory: $class"
    }
}

$legacyRoots = @('Adapters', 'Core', 'MultiObserver', 'Platform', 'Security', 'StaticIL', 'Fakes')
foreach ($legacyRoot in $legacyRoots) {
    if (Test-Path -LiteralPath (Join-Path $repoRoot (Join-Path 'WhitelistTests' $legacyRoot))) {
        throw "Legacy test root still exists: $legacyRoot"
    }
}

$programPath = Join-Path $repoRoot 'WhitelistTests\Program.cs'
$program = Get-Content -LiteralPath $programPath -Raw
if ([regex]::Matches($program, 'static\s+int\s+Main\s*\(').Count -ne 1) {
    throw 'Expected exactly one Program.Main entry.'
}
if ($program -notmatch 'EvidenceClass\.PureMemory' -or
    $program -notmatch 'EvidenceClass\.StaticIL' -or
    $program -notmatch 'EvidenceClass\.BuildArtifact' -or
    $program -notmatch 'EvidenceClass\.Runtime') {
    throw 'Program.Main does not expose all four Evidence Class stages.'
}

$counts = foreach ($class in $classes) {
    $classPath = Join-Path $evidenceRoot $class
    [pscustomobject]@{
        EvidenceClass = $class
        CSharpFiles = @(Get-ChildItem -LiteralPath $classPath -Recurse -File -Filter '*.cs').Count
    }
}
if ($counts | Where-Object { $_.CSharpFiles -eq 0 }) {
    throw 'Every Evidence Class must have a physical C# evidence/status file.'
}

$counts | Format-Table -AutoSize
Write-Output 'EVIDENCE_CLASS_LAYOUT_PASS'
