[CmdletBinding()]
param(
    [ValidateSet('Stdio', 'Http')]
    [string]$Mode = 'Stdio',

    [string]$ConfigPath = (Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'config') 'unifi-mcp.settings.json'),

    [string]$SiteManagerSecretName = 'UniFiSiteManagerApiKey',

    [string]$NetworkSecretName = 'UniFiNetworkApiKey',

    [string]$MutationApprovalSecretName = 'UniFiMutationApprovalKey',

    [string]$Vault,

    [string]$HttpAuthTokenSecretName,

    [string]$HttpUrls = 'http://127.0.0.1:8765',

    [string]$HttpAllowedOrigins
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-SecretSecureString {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $parameters = @{ Name = $Name }
    if (-not [string]::IsNullOrWhiteSpace($Vault)) {
        $parameters.Vault = $Vault
    }

    $secret = Get-Secret @parameters
    if ($secret -isnot [System.Security.SecureString]) {
        throw "Secret '$Name' must be stored as a SecureString."
    }

    return $secret
}

function Set-ProcessSecret {
    param(
        [Parameter(Mandatory)]
        [string]$EnvironmentVariable,

        [Parameter(Mandatory)]
        [System.Security.SecureString]$Secret
    )

    $pointer = [IntPtr]::Zero
    try {
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
        $plainText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        [Environment]::SetEnvironmentVariable($EnvironmentVariable, $plainText, 'Process')
    }
    finally {
        $plainText = $null
        if ($pointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        }
    }
}

function Save-EnvironmentVariable {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not $script:originalEnvironment.ContainsKey($Name)) {
        $script:originalEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process')
        $script:modifiedVariables.Add($Name)
    }
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$resolvedConfigPath = [IO.Path]::GetFullPath($ConfigPath)
$projectName = if ($Mode -eq 'Stdio') { 'UnifiMcp.Stdio' } else { 'UnifiMcp.Http' }
$executableName = if ($IsWindows) { "$projectName.exe" } else { $projectName }
$packagedExecutablePath = Join-Path (Join-Path $repositoryRoot $Mode.ToLowerInvariant()) $executableName
$projectRoot = Join-Path (Join-Path $repositoryRoot 'src') $projectName
$assemblyDirectory = Join-Path (Join-Path (Join-Path $projectRoot 'bin') 'Release') 'net8.0'
$assemblyPath = Join-Path $assemblyDirectory "$projectName.dll"
$applicationPath = if (Test-Path -LiteralPath $packagedExecutablePath -PathType Leaf) {
    $packagedExecutablePath
}
else {
    $assemblyPath
}
$script:modifiedVariables = [Collections.Generic.List[string]]::new()
$script:originalEnvironment = @{}

try {
    Import-Module Microsoft.PowerShell.SecretManagement -ErrorAction Stop

    if (-not (Test-Path -LiteralPath $resolvedConfigPath -PathType Leaf)) {
        throw "UniFi MCP configuration file '$resolvedConfigPath' was not found."
    }

    if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
        throw "UniFi MCP application '$applicationPath' was not found."
    }

    Save-EnvironmentVariable -Name 'UNIFI_SITE_MANAGER_API_KEY'
    Set-ProcessSecret -EnvironmentVariable 'UNIFI_SITE_MANAGER_API_KEY' -Secret (Get-SecretSecureString -Name $SiteManagerSecretName)
    Save-EnvironmentVariable -Name 'UNIFI_NETWORK_API_KEY'
    Set-ProcessSecret -EnvironmentVariable 'UNIFI_NETWORK_API_KEY' -Secret (Get-SecretSecureString -Name $NetworkSecretName)
    Save-EnvironmentVariable -Name 'UNIFI_MCP_MUTATION_APPROVAL_KEY'
    Set-ProcessSecret -EnvironmentVariable 'UNIFI_MCP_MUTATION_APPROVAL_KEY' -Secret (Get-SecretSecureString -Name $MutationApprovalSecretName)

    if ($Mode -eq 'Http') {
        Save-EnvironmentVariable -Name 'UNIFI_MCP_HTTP_URLS'
        [Environment]::SetEnvironmentVariable('UNIFI_MCP_HTTP_URLS', $HttpUrls, 'Process')

        if (-not [string]::IsNullOrWhiteSpace($HttpAllowedOrigins)) {
            Save-EnvironmentVariable -Name 'UNIFI_MCP_HTTP_ALLOWED_ORIGINS'
            [Environment]::SetEnvironmentVariable('UNIFI_MCP_HTTP_ALLOWED_ORIGINS', $HttpAllowedOrigins, 'Process')
        }

        if (-not [string]::IsNullOrWhiteSpace($HttpAuthTokenSecretName)) {
            Save-EnvironmentVariable -Name 'UNIFI_MCP_HTTP_AUTH_TOKEN'
            Set-ProcessSecret -EnvironmentVariable 'UNIFI_MCP_HTTP_AUTH_TOKEN' -Secret (Get-SecretSecureString -Name $HttpAuthTokenSecretName)
        }
    }

    if ($applicationPath -eq $assemblyPath) {
        & dotnet $applicationPath --config $resolvedConfigPath
    }
    else {
        & $applicationPath --config $resolvedConfigPath
    }
    if ($LASTEXITCODE -ne 0) {
        throw "UniFi MCP exited with code $LASTEXITCODE."
    }
}
finally {
    foreach ($variable in $script:modifiedVariables) {
        [Environment]::SetEnvironmentVariable($variable, $script:originalEnvironment[$variable], 'Process')
    }
}
