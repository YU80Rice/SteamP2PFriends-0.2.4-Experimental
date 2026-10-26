param(
    [Parameter(Mandatory = $true)][string]$EvidenceDir,
    [Parameter(Mandatory = $true)][string]$HostDll
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = Join-Path $PSScriptRoot 'bin/Release/SteamP2PFriends.WhitelistTests.exe'
if (-not [IO.Path]::IsPathRooted($EvidenceDir) -or -not [IO.Path]::IsPathRooted($HostDll)) { throw 'EvidenceDir and HostDll must be absolute paths.' }
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $HostDll)) { throw 'Current test executable or native host is missing.' }
New-Item -ItemType Directory -Path $EvidenceDir -Force | Out-Null
$ledger = Join-Path $EvidenceDir 'registered-tests.tsv'
$stdout = Join-Path $EvidenceDir 'main.stdout.txt'
$stderr = Join-Path $EvidenceDir 'main.stderr.txt'
$previous = @{}
foreach ($name in @('SPF_NIL01_DISPATCH','SPF_NIL01_DOTNET','SPF_NIL01_HOST_DLL','SPF_TEST_RUN_LEDGER')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
$env:SPF_NIL01_DISPATCH = 'CoreCLR10.0.9-x86'
$env:SPF_NIL01_DOTNET = 'C:/Program Files (x86)/dotnet/dotnet.exe'
$env:SPF_NIL01_HOST_DLL = (Resolve-Path $HostDll).Path
$env:SPF_TEST_RUN_LEDGER = $ledger
$proc = Start-Process -FilePath $exe -WorkingDirectory $repo -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$rows = @(Import-Csv -LiteralPath $ledger -Delimiter "`t")
if ($rows.Count -eq 0) { throw 'The formal Main produced no per-test ledger rows.' }
if (@($rows.runId | Select-Object -Unique).Count -ne 1 -or [string]::IsNullOrWhiteSpace($rows[0].runId)) { throw 'Ledger rows do not share one formal runId.' }
for ($i = 0; $i -lt $rows.Count; $i++) { if ([int]$rows[$i].ordinal -ne ($i + 1)) { throw "Ledger ordinal gap at row $($i + 1)." } }
$lookup = @($rows | Where-Object { $_.callback -like '*Test_NIL01_RealNativeLookupContract*' -and $_.executionMode -eq 'CoreCLR-child' }).Count
$binding = @($rows | Where-Object { $_.callback -like '*Test_GBA01_NativeFirstObserveBindingAdmission*' -and $_.executionMode -eq 'CoreCLR-child' }).Count
if ($lookup -ne 1 -or $binding -ne 1) { throw "Required child routes incomplete: NIL01=$lookup GBA01=$binding." }
$main = Get-Content -LiteralPath $stdout -Raw -Encoding UTF8
if ($main -notmatch 'Final Result: (\d+)/(\d+) PASS \(Failed: 0\)' -or [int]$Matches[1] -ne $rows.Count -or $proc.ExitCode -ne 0) {
    throw "Main/ledger mismatch or failure: exit=$($proc.ExitCode), ledger=$($rows.Count). See $stdout and $stderr."
}
Write-Output "FORMAL_MAIN runId=$($rows[0].runId) exit=$($proc.ExitCode) registered=$($rows.Count) framework-local=$($rows.Count-2) CoreCLR-x86=2 ledger=$ledger stdout=$stdout stderr=$stderr"
}
finally {
    foreach ($name in $previous.Keys) {
        if ($null -eq $previous[$name]) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
        else { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    }
}
exit 0
