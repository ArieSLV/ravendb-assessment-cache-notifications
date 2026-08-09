param(
    [string] $DotNetPath = "",
    [string] $Url = "http://127.0.0.1:8081",
    [string] $Database = "CacheConsistencyCheck",
    [int] $Clients = 48,
    [int] $Iterations = 20,
    [int] $DeadlineMs = 3000,
    [int] $PollMs = 25,
    [int] $CaptureSockets = 4,
    [string] $EvidenceDir = "",
    [int] $ExpectMinIncidents = 0
)

. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$assessmentRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "assessment\cache-consistency-check\CacheConsistencyCheck.csproj"

if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
    $EvidenceDir = Join-Path $assessmentRoot "EVIDENCE"
}

$DotNetPath = Initialize-DotNetForScript -DotNetPath $DotNetPath

Invoke-CheckedNativePassthrough `
    -FilePath $DotNetPath `
    -Arguments @(
        "run",
        "--project", $project,
        "-c", "Release",
        "--",
        "--url", $Url,
        "--database", $Database,
        "--clients", $Clients,
        "--iterations", $Iterations,
        "--deadline-ms", $DeadlineMs,
        "--poll-ms", $PollMs,
        "--capture-sockets", $CaptureSockets,
        "--evidence-dir", $EvidenceDir,
        "--expect-min-incidents", $ExpectMinIncidents
    ) `
    -Description "Running cache consistency workload..."
