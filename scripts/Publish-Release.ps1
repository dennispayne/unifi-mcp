[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',

    [string[]]$RuntimeIdentifiers = @(),

    [switch]$SkipValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$artifactsRoot = Join-Path (Join-Path $repositoryRoot 'artifacts') 'release'
$expectedArtifactsRoot = [IO.Path]::GetFullPath((Join-Path (Join-Path $repositoryRoot 'artifacts') 'release'))
if ([IO.Path]::GetFullPath($artifactsRoot) -ne $expectedArtifactsRoot) {
    throw 'Refusing to clean an unexpected artifacts path.'
}

if ($RuntimeIdentifiers.Count -eq 0) {
    $RuntimeIdentifiers = if ($IsWindows) {
        @('win-x64', 'win-arm64')
    }
    elseif ($IsMacOS) {
        @('osx-x64', 'osx-arm64')
    }
    else {
        @('linux-x64', 'linux-arm64')
    }
}

if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $artifactsRoot)

Push-Location $repositoryRoot
try {
    if (-not $SkipValidation) {
        $validationOutputRoot = Join-Path $artifactsRoot 'validation-build'
        $validationOutputRootWithSeparator = $validationOutputRoot + [IO.Path]::DirectorySeparatorChar
        & dotnet build (Join-Path $repositoryRoot 'UnifiMcp.slnx') `
            --configuration Release `
            "-p:Version=$Version" `
            "-p:BaseOutputPath=$validationOutputRootWithSeparator"
        if ($LASTEXITCODE -ne 0) {
            throw "Release build failed with exit code $LASTEXITCODE."
        }

        & dotnet run `
            --project (Join-Path (Join-Path (Join-Path $repositoryRoot 'tests') 'Unifi.Mcp.Client.SmokeTests') 'Unifi.Mcp.Client.SmokeTests.csproj') `
            --configuration Release `
            --no-build `
            "-p:BaseOutputPath=$validationOutputRootWithSeparator"
        if ($LASTEXITCODE -ne 0) {
            throw "Smoke tests failed with exit code $LASTEXITCODE."
        }
    }

    foreach ($runtimeIdentifier in $RuntimeIdentifiers) {
        $packageName = "unifi-mcp-v$Version-$runtimeIdentifier"
        $packageRoot = Join-Path $artifactsRoot $packageName

        foreach ($hostDefinition in @(
            @{
                Name = 'stdio'
                Project = Join-Path (Join-Path (Join-Path $repositoryRoot 'src') 'UnifiMcp.Stdio') 'UnifiMcp.Stdio.csproj'
                Executable = 'UnifiMcp.Stdio'
            },
            @{
                Name = 'http'
                Project = Join-Path (Join-Path (Join-Path $repositoryRoot 'src') 'UnifiMcp.Http') 'UnifiMcp.Http.csproj'
                Executable = 'UnifiMcp.Http'
            }
        )) {
            $publishPath = Join-Path $packageRoot $hostDefinition.Name
            & dotnet publish $hostDefinition.Project `
                --configuration Release `
                --runtime $runtimeIdentifier `
                --self-contained true `
                --output $publishPath `
                "-p:Version=$Version" `
                -p:PublishSingleFile=true `
                -p:PublishTrimmed=false `
                -p:DebugType=None `
                -p:DebugSymbols=false
            if ($LASTEXITCODE -ne 0) {
                throw "Publishing $($hostDefinition.Name) for $runtimeIdentifier failed with exit code $LASTEXITCODE."
            }

            if (-not $runtimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
                & chmod +x (Join-Path $publishPath $hostDefinition.Executable)
                if ($LASTEXITCODE -ne 0) {
                    throw "Setting executable permissions for $($hostDefinition.Name) failed with exit code $LASTEXITCODE."
                }
            }
        }

        [void](New-Item -ItemType Directory -Path (Join-Path $packageRoot 'config'))
        [void](New-Item -ItemType Directory -Path (Join-Path $packageRoot 'scripts'))
        Copy-Item (Join-Path (Join-Path $repositoryRoot 'config') 'unifi-mcp.settings.example.json') (Join-Path $packageRoot 'config')
        Copy-Item (Join-Path (Join-Path $repositoryRoot 'config') 'guardrails.example.json') (Join-Path $packageRoot 'config')
        Copy-Item (Join-Path (Join-Path $repositoryRoot 'scripts') 'Start-UnifiMcp.ps1') (Join-Path $packageRoot 'scripts')
        Copy-Item (Join-Path (Join-Path $repositoryRoot 'scripts') 'Initialize-UnifiMcpSecrets.ps1') (Join-Path $packageRoot 'scripts')
        Copy-Item (Join-Path (Join-Path $repositoryRoot 'scripts') 'New-UnifiMutationApproval.ps1') (Join-Path $packageRoot 'scripts')
        Copy-Item @(
            (Join-Path $repositoryRoot 'README.md'),
            (Join-Path $repositoryRoot 'SECURITY.md'),
            (Join-Path $repositoryRoot 'LICENSE')
        ) $packageRoot
        Copy-Item (Join-Path $repositoryRoot 'docs') (Join-Path $packageRoot 'docs') -Recurse

        [pscustomobject]@{
            name = 'unifi-mcp'
            version = $Version
            runtimeIdentifier = $runtimeIdentifier
        } | ConvertTo-Json | Set-Content (Join-Path $packageRoot 'version.json') -Encoding utf8NoBOM

        if ($runtimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
            $archivePath = Join-Path $artifactsRoot "$packageName.zip"
            Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal
        }
        else {
            $archivePath = Join-Path $artifactsRoot "$packageName.tar.gz"
            & tar -czf $archivePath -C $artifactsRoot $packageName
            if ($LASTEXITCODE -ne 0) {
                throw "Creating archive for $runtimeIdentifier failed with exit code $LASTEXITCODE."
            }
        }
    }

    $checksumLines = Get-ChildItem -LiteralPath $artifactsRoot -File |
        Where-Object { $_.Name.EndsWith('.zip') -or $_.Name.EndsWith('.tar.gz') } |
        Sort-Object Name |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($_.Name)"
        }
    $checksumLines | Set-Content (Join-Path $artifactsRoot 'SHA256SUMS.txt') -Encoding ascii
}
finally {
    Pop-Location
}

Write-Host "Release packages written to '$artifactsRoot'."
