[CmdletBinding()]
param(
    [string]$Vault = 'LocalStore',

    [string]$SiteManagerSecretName = 'UniFiSiteManagerApiKey',

    [string]$NetworkSecretName = 'UniFiNetworkApiKey',

    [string]$MutationApprovalSecretName = 'UniFiMutationApprovalKey',

    [switch]$Unattended
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($moduleName in 'Microsoft.PowerShell.SecretManagement', 'Microsoft.PowerShell.SecretStore') {
    if (-not (Get-Module -ListAvailable -Name $moduleName)) {
        throw "Module '$moduleName' is required. Install it with: Install-Module Microsoft.PowerShell.SecretManagement,Microsoft.PowerShell.SecretStore -Scope CurrentUser"
    }
}

Import-Module Microsoft.PowerShell.SecretManagement
Import-Module Microsoft.PowerShell.SecretStore

if (-not (Get-SecretVault -Name $Vault -ErrorAction SilentlyContinue)) {
    Register-SecretVault -Name $Vault -ModuleName Microsoft.PowerShell.SecretStore -DefaultVault
}

if ($Unattended) {
    Write-Warning 'Unattended mode allows any process running as your Windows user to retrieve this vault content without an additional password.'
    Set-SecretStoreConfiguration -Authentication None -Interaction None -Confirm:$false
}
else {
    Set-SecretStoreConfiguration -Authentication Password -Interaction Prompt -Confirm:$false
}

$siteManagerKey = Read-Host 'Site Manager API key' -AsSecureString
$networkKey = Read-Host 'Network API key' -AsSecureString
$approvalKey = $null

try {
    Set-Secret -Vault $Vault -Name $SiteManagerSecretName -Secret $siteManagerKey
    Set-Secret -Vault $Vault -Name $NetworkSecretName -Secret $networkKey
    $approvalKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) |
        ConvertTo-SecureString -AsPlainText -Force
    Set-Secret -Vault $Vault -Name $MutationApprovalSecretName -Secret $approvalKey
}
finally {
    $siteManagerKey.Dispose()
    $networkKey.Dispose()
    if ($approvalKey) {
        $approvalKey.Dispose()
    }
}

if (-not (Test-SecretVault -Name $Vault)) {
    throw "Secret vault '$Vault' failed its connectivity test."
}

Write-Host "Stored both UniFi API keys and a generated mutation approval key in SecretManagement vault '$Vault'."
