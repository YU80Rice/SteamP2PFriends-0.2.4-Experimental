param([Parameter(Mandatory = $true)][ValidateSet('RED', 'GREEN')][string]$CaseName)
$ErrorActionPreference = 'Stop'
$experiment = $PSScriptRoot
$caseRoot = Join-Path $experiment $CaseName.ToLowerInvariant()
$caseLogRoot = Join-Path $experiment ("logs/$CaseName")
New-Item -ItemType Directory -Path $caseLogRoot -Force | Out-Null
$attempt = 1
while (Test-Path -LiteralPath (Join-Path $caseLogRoot "attempt-$attempt")) { $attempt++ }
$logRoot = Join-Path $caseLogRoot "attempt-$attempt"
New-Item -ItemType Directory -Path $logRoot | Out-Null

function Invoke-LoggedProcess([string]$Tag, [string]$Executable, [string[]]$Arguments) {
    $started = [DateTime]::UtcNow.ToString('o')
    $stdout = Join-Path $logRoot "$Tag.stdout.txt"
    $stderr = Join-Path $logRoot "$Tag.stderr.txt"
    $command = '"' + $Executable + '" ' + ($Arguments -join ' ')
    [IO.File]::WriteAllText((Join-Path $logRoot "$Tag.command.txt"), $command, [Text.UTF8Encoding]::new($false))
    & $Executable @Arguments 1> $stdout 2> $stderr
    $exitCode = $LASTEXITCODE
    $ended = [DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $logRoot "$Tag.result.txt"), "startedUtc=$started`nendedUtc=$ended`nexitCode=$exitCode`n", [Text.UTF8Encoding]::new($false))
    if ($exitCode -ne 0) { throw "$Tag failed with exit=$exitCode; see $logRoot" }
}

$dotnet = 'C:/Program Files/dotnet/dotnet.exe'
$targetRunner = Join-Path $experiment 'run-sfp03-target.ps1'
Push-Location $caseRoot
try {
    $productionArguments = @('msbuild', 'SteamP2PFriends.csproj', '/t:Build', '/p:Configuration=Release', '/v:minimal')
    if ($CaseName -eq 'RED') { $productionArguments += '/p:WarningsNotAsErrors=CS0414' }
    Invoke-LoggedProcess 'production-build' $dotnet $productionArguments
    Invoke-LoggedProcess 'whitelist-build' $dotnet @('msbuild', 'WhitelistTests/SteamP2PFriends.WhitelistTests.csproj', '/t:Build', '/p:Configuration=Release', '/v:minimal')
    Invoke-LoggedProcess 'target' 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $targetRunner, '-RepoRoot', $caseRoot, '-CaseName', $CaseName)
} finally { Pop-Location }
