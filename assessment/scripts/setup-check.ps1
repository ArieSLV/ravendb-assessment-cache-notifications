param(
    [string] $DotNetPath = ""
)

. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "assessment\cache-consistency-check\CacheConsistencyCheck.csproj"
$serverProject = Join-Path $repoRoot "src\Raven.Server\Raven.Server.csproj"

$DotNetPath = Initialize-DotNetForScript -DotNetPath $DotNetPath

$licensePath = Join-Path $repoRoot "license.json"
if (Test-Path $licensePath) {
    Write-Host "License: license.json found in the repository root."
}
else {
    Write-Host "License: MISSING. RavenDB needs a license to run."
    Write-Host "  Request a free developer license at https://ravendb.net/license/request/dev"
    Write-Host "  and save it as license.json in the root of this repository."
    exit 1
}

$null = Invoke-CheckedNativeCommand `
    -FilePath $DotNetPath `
    -Arguments @("build", $project, "-c", "Release", "/nodeReuse:false") `
    -Description "Building cache consistency workload..." `
    -SummarizeMsBuildErrors

$null = Invoke-CheckedNativeCommand `
    -FilePath $DotNetPath `
    -Arguments @("build", $serverProject, "-c", "Release", "/nodeReuse:false") `
    -Description "Building Raven.Server..." `
    -SummarizeMsBuildErrors

Write-Host "Setup check completed."
