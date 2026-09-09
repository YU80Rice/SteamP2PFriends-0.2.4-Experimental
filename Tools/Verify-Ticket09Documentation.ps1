$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$versionPropsPath = Join-Path $repoRoot 'Build\Version.props'
$versionProps = [xml](Get-Content -LiteralPath $versionPropsPath -Raw)
$namespace = New-Object System.Xml.XmlNamespaceManager($versionProps.NameTable)
$namespace.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')

function Get-PropertyValue([string]$name) {
    $node = $versionProps.SelectSingleNode('//msb:' + $name, $namespace)
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Missing build metadata property: $name"
    }
    return $node.InnerText
}

$version = Get-PropertyValue 'SteamP2PFriendsVersion'
$releaseChannel = Get-PropertyValue 'SteamP2PFriendsReleaseChannel'
$pluginGuid = Get-PropertyValue 'SteamP2PFriendsPluginGuid'
$defaultCaseId = (Get-PropertyValue 'SteamP2PFriendsDefaultCaseId').Replace('$(SteamP2PFriendsVersion)', $version).Replace('$(SteamP2PFriendsReleaseChannel)', $releaseChannel)

$documents = @(
    @{ Path = 'README.md'; Required = @($version, 'Runtime 状态仍为 `PENDING`', '历史运行证据（不属于 0.2.4.8 当前验收）') },
    @{ Path = 'docs\architecture\build-fingerprint-artifact-evidence.md'; Required = @('Build/Version.props', $version, $pluginGuid, $defaultCaseId, 'evidence=self-reported', 'independent-artifact-verification', 'Runtime 仍需真实') },
    @{ Path = 'docs\architecture\migration-manifest.md'; Required = @($version, $pluginGuid, 'Batch 9：Build Fingerprint 与独立产物关联', 'Runtime | PENDING') },
    @{ Path = '.scratch\structure-baseline-0-2-4-8\issues\09-build-fingerprint-artifact-evidence.md'; Required = @($version, 'BuildArtifact 测试、Release 构建和独立审核通过') },
    @{ Path = 'audit\2026-08-27\Implementation-0.2.4.8-1234.md'; Required = @($version, $pluginGuid, $defaultCaseId, 'Runtime：`PENDING`', '元数据来源：`Build/Version.props`') }
)

foreach ($document in $documents) {
    $path = Join-Path $repoRoot $document.Path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Ticket 09 metadata document: $($document.Path)"
    }

    # 文档本体无 BOM:PS5.1 Get-Content 会按 ANSI 读取并破坏中文 Ordinal 匹配。
    # 与本脚本解析期 UTF-8 BOM 配套,内容读取同样显式 UTF-8(2337 §四镜像口径)。
    $content = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
    foreach ($required in $document.Required) {
        if ($content.IndexOf($required, [System.StringComparison]::Ordinal) -lt 0) {
            throw "Document metadata mismatch: $($document.Path) is missing '$required'"
        }
    }
}

[pscustomobject]@{
    Evidence = 'ticket09-documentation-metadata-consistency'
    MetadataSource = 'Build/Version.props'
    Version = $version
    ReleaseChannel = $releaseChannel
    PluginGuid = $pluginGuid
    DefaultCaseId = $defaultCaseId
    DocumentsChecked = $documents.Count
    Result = 'PASS'
} | Format-List

Write-Output 'TICKET09_DOCUMENTATION_METADATA_PASS'
