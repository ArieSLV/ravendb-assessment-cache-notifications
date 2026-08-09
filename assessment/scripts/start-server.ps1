param(
    [string] $DotNetPath = "",
    [string] $Url = "http://127.0.0.1:8081",
    [string] $DataDir = ""
)

. (Join-Path $PSScriptRoot "common.ps1")

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$assessmentRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$serverProject = Join-Path $repoRoot "src\Raven.Server\Raven.Server.csproj"

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    $DataDir = Join-Path $assessmentRoot "server-data"
}

$licensePath = Join-Path $repoRoot "license.json"
if (-not (Test-Path $licensePath)) {
    Write-Host ""
    Write-Host "No license found at $licensePath"
    Write-Host ""
    Write-Host "RavenDB needs a license to run. A free developer license is enough:"
    Write-Host "  https://ravendb.net/license/request/dev"
    Write-Host ""
    Write-Host "Save the file you receive as license.json in the root of this repository,"
    Write-Host "next to RavenDB.sln, and run this script again."
    Write-Host ""
    exit 1
}

$DotNetPath = Initialize-DotNetForScript -DotNetPath $DotNetPath

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

Write-Host "Starting Raven.Server at $Url"
Write-Host "DataDir: $DataDir"

Invoke-CheckedNativePassthrough `
    -FilePath $DotNetPath `
    -Arguments @(
        "run",
        "--project", $serverProject,
        "-c", "Release",
        "--",
        "--ServerUrl=$Url",
        "--PublicServerUrl=$Url",
        "--DataDir=$DataDir",
        "--RunInMemory=false",
        "--Setup.Mode=None",
        "--Security.UnsecuredAccessAllowed=PublicNetwork",
        "--Features.Availability=Experimental",
        "--License.Path=$licensePath"
    ) `
    -Description "Running Raven.Server..."
