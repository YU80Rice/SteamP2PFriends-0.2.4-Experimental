param(
    [string]$Path = (Join-Path $PSScriptRoot '..\bin\Release\SteamP2PFriends.dll'),
    [string]$ExpectedCaseId = '',
    [string]$LogPath = ''
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$versionPropsPath = Join-Path $PSScriptRoot '..\Build\Version.props'
$versionProps = [xml](Get-Content -LiteralPath $versionPropsPath -Raw)
$namespace = New-Object System.Xml.XmlNamespaceManager($versionProps.NameTable)
$namespace.AddNamespace('msb', 'http://schemas.microsoft.com/developer/msbuild/2003')
$expectedVersion = $versionProps.SelectSingleNode('//msb:SteamP2PFriendsVersion', $namespace).InnerText
$expectedReleaseChannel = $versionProps.SelectSingleNode('//msb:SteamP2PFriendsReleaseChannel', $namespace).InnerText
$expectedPluginGuid = $versionProps.SelectSingleNode('//msb:SteamP2PFriendsPluginGuid', $namespace).InnerText
$expectedDefaultCaseId = $versionProps.SelectSingleNode('//msb:SteamP2PFriendsDefaultCaseId', $namespace).InnerText
$expectedDefaultCaseId = $expectedDefaultCaseId.Replace('$(SteamP2PFriendsVersion)', $expectedVersion).Replace('$(SteamP2PFriendsReleaseChannel)', $expectedReleaseChannel)
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedPath).FileVersion
$hash = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToUpperInvariant()
$assembly = [Reflection.Assembly]::LoadFile($resolvedPath)
$assemblyVersion = $assembly.GetName().Version.ToString()
$mvid = $assembly.ManifestModule.ModuleVersionId.ToString('D')
$metadata = @{}

foreach ($attribute in $assembly.GetCustomAttributesData()) {
    if ($attribute.AttributeType.FullName -ne 'System.Reflection.AssemblyMetadataAttribute') {
        continue
    }

    $arguments = $attribute.ConstructorArguments
    if ($arguments.Count -eq 2) {
        $metadata[[string]$arguments[0].Value] = [string]$arguments[1].Value
    }
}

$version = $metadata['SteamP2PFriendsVersion']
$pluginGuid = $metadata['SteamP2PFriendsPluginGuid']
$artifactDefaultCaseId = $metadata['SteamP2PFriendsDefaultCaseId']
$caseId = if ($ExpectedCaseId) { $ExpectedCaseId } else { $artifactDefaultCaseId }
$caseIdSource = if ($ExpectedCaseId) { 'expected-case-id' } else { 'artifact-metadata' }
$caseIdAssociation = if ($ExpectedCaseId) { 'pending-log-correlation' } else { 'artifact-metadata' }
$checks = @(
    ($version -eq $expectedVersion),
    ($assemblyVersion -eq $expectedVersion),
    ($fileVersion -eq $expectedVersion),
    ($pluginGuid -eq $expectedPluginGuid),
    ($artifactDefaultCaseId -eq $expectedDefaultCaseId),
    (-not [string]::IsNullOrWhiteSpace($caseId)),
    ($caseId -match '^[A-Za-z0-9._-]{1,96}$'),
    ($hash -match '^[0-9A-F]{64}$'),
    ($mvid -ne [Guid]::Empty.ToString('D'))
)

if ($ExpectedCaseId) {
    if ([string]::IsNullOrWhiteSpace($LogPath) -or -not (Test-Path -LiteralPath $LogPath)) {
        $checks += $false
    }
    else {
        $log = Get-Content -LiteralPath $LogPath -Raw
        $logMatchesFingerprint = $false
        $casePattern = '(^|\s)caseId=' + [regex]::Escape($ExpectedCaseId) + '(\s|$)'
        $hashPattern = '(^|\s)dllSha256=' + [regex]::Escape($hash) + '(\s|$)'
        $versionPattern = '(^|\s)version=' + [regex]::Escape($version) + '(\s|$)'
        $assemblyVersionPattern = '(^|\s)assemblyVersion=' + [regex]::Escape($assemblyVersion) + '(\s|$)'
        $fileVersionPattern = '(^|\s)fileVersion=' + [regex]::Escape($fileVersion) + '(\s|$)'
        $mvidPattern = '(^|\s)mvid=' + [regex]::Escape($mvid) + '(\s|$)'
        $guidPattern = '(^|\s)pluginGuid=' + [regex]::Escape($pluginGuid) + '(\s|$)'
        foreach ($line in ($log -split '\r?\n')) {
            if ($line -match $casePattern -and
                $line -match $hashPattern -and
                $line -match $versionPattern -and
                $line -match $assemblyVersionPattern -and
                $line -match $fileVersionPattern -and
                $line -match $mvidPattern -and
                $line -match $guidPattern) {
                $logMatchesFingerprint = $true
                break
            }
        }
        $checks += $logMatchesFingerprint
        if ($logMatchesFingerprint) {
            $caseIdAssociation = 'log-self-report-and-artifact-hash'
        }
    }
}

$result = [pscustomobject]@{
    Evidence = 'independent-artifact-verification'
    Path = $resolvedPath
    Version = $version
    AssemblyVersion = $assemblyVersion
    FileVersion = $fileVersion
    MVID = $mvid
    DLL_SHA256 = $hash
    PluginGuid = $pluginGuid
    CaseId = $caseId
    CaseIdSource = $caseIdSource
    CaseIdAssociation = $caseIdAssociation
    ArtifactDefaultCaseId = $artifactDefaultCaseId
    MetadataSource = 'Build/Version.props'
    Result = if (($checks -notcontains $false) -and ($checks.Count -gt 0)) { 'PASS' } else { 'FAIL' }
}

$result | Format-List
if ($result.Result -ne 'PASS') {
    throw 'Independent Artifact Verification failed.'
}

Write-Output 'INDEPENDENT_ARTIFACT_VERIFICATION_PASS'
