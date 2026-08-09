$ErrorActionPreference = "Stop"

function Resolve-DotNetCommand {
    param(
        [string] $DotNetPath
    )

    if ([string]::IsNullOrWhiteSpace($DotNetPath) -eq $false) {
        return $DotNetPath
    }

    if ([string]::IsNullOrWhiteSpace($env:RAVEN_DOTNET) -eq $false) {
        return $env:RAVEN_DOTNET
    }

    return "dotnet"
}

function Get-DotNetSdkVersion {
    param(
        [string] $DotNetPath
    )

    try {
        $versionOutput = & $DotNetPath --version 2>&1
        $exitCode = $LASTEXITCODE
    }
    catch {
        throw "Failed to run '$DotNetPath --version'. Pass -DotNetPath or set RAVEN_DOTNET to a working .NET SDK command."
    }

    if ($exitCode -ne 0) {
        $message = ($versionOutput | Out-String).Trim()
        throw "Failed to run '$DotNetPath --version' (exit code $exitCode). $message"
    }

    return (($versionOutput | Select-Object -First 1) -as [string]).Trim()
}

function Use-ScriptLocalSdkResolution {
    param(
        [string] $ActiveSdkVersion
    )

    $configuredSdkPath = $env:MSBuildSDKsPath
    if ([string]::IsNullOrWhiteSpace($configuredSdkPath)) {
        return
    }

    if ($configuredSdkPath -like "*$ActiveSdkVersion*") {
        Write-Host "MSBuildSDKsPath is set and appears to match active SDK $ActiveSdkVersion. Temporarily clearing it only for this script process so the .NET SDK resolves its own MSBuild SDKs."
    }
    else {
        Write-Host "MSBuildSDKsPath is set and does not match active SDK $ActiveSdkVersion. Temporarily clearing it only for this script process to avoid pinning MSBuild to an incompatible SDK."
    }

    $env:MSBuildSDKsPath = $null
}

function Initialize-DotNetForScript {
    param(
        [string] $DotNetPath
    )

    $resolvedDotNetPath = Resolve-DotNetCommand -DotNetPath $DotNetPath
    $sdkVersion = Get-DotNetSdkVersion -DotNetPath $resolvedDotNetPath
    Use-ScriptLocalSdkResolution -ActiveSdkVersion $sdkVersion
    Write-Host ".NET SDK: $sdkVersion"
    return $resolvedDotNetPath
}

function Write-MsBuildErrorSummary {
    param(
        [object[]] $Output
    )

    $errors = @()
    foreach ($item in $Output) {
        $line = ($item | Out-String).TrimEnd()
        if ($line -match ":\s*error\s" -and $line -notmatch "NU\d{4}") {
            $errors += ($line -replace "\s+\[[^\]]+\.csproj\]\s*$", "")
        }
    }

    if ($errors.Count -eq 0) {
        Write-Host "No canonical MSBuild error lines were found in the captured output."
        return
    }

    Write-Host "MSBuild error summary:"
    $errors |
        Group-Object |
        Sort-Object Count -Descending |
        ForEach-Object {
            if ($_.Count -eq 1) {
                Write-Host "  $($_.Name)"
            }
            else {
                Write-Host "  $($_.Name) (x$($_.Count))"
            }
        }
}

function Invoke-CheckedNativeCommand {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $Description,
        [switch] $SummarizeMsBuildErrors
    )

    Write-Host $Description

    try {
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    catch {
        Write-Host "$Description failed before an exit code was returned."
        Write-Host $_
        exit 1
    }

    if ($exitCode -ne 0) {
        Write-Host "$Description failed with exit code $exitCode."
        if ($SummarizeMsBuildErrors) {
            Write-MsBuildErrorSummary -Output $output
        }
        else {
            $output | ForEach-Object { Write-Host $_ }
        }
        exit $exitCode
    }

    return $output
}

function Invoke-CheckedNativePassthrough {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $Description
    )

    Write-Host $Description
    & $FilePath @Arguments
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Write-Host "$Description failed with exit code $exitCode."
        exit $exitCode
    }
}
