[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Scope,

    [Parameter(Mandatory)]
    [ValidateSet('POST', 'PUT', 'PATCH', 'DELETE')]
    [string]$Method,

    [Parameter(Mandatory)]
    [string]$Path,

    [string]$BodyPath,

    [string]$Vault,

    [string]$SecretName = 'UniFiMutationApprovalKey',

    [ValidateRange(30, 300)]
    [int]$ExpiresInSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.SecretManagement -ErrorAction Stop

$parameters = @{ Name = $SecretName }
if (-not [string]::IsNullOrWhiteSpace($Vault)) {
    $parameters.Vault = $Vault
}

$secureKey = Get-Secret @parameters
if ($secureKey -isnot [Security.SecureString]) {
    throw "Secret '$SecretName' must be stored as a SecureString."
}

$bodyBytes = [byte[]]::new(0)
if (-not [string]::IsNullOrWhiteSpace($BodyPath)) {
    $resolvedBodyPath = (Resolve-Path -LiteralPath $BodyPath).Path
    $bodyDocument = [Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($resolvedBodyPath))
    try {
        $bodyBytes = [Text.Json.JsonSerializer]::SerializeToUtf8Bytes($bodyDocument.RootElement)
    }
    finally {
        $bodyDocument.Dispose()
    }
}

$pointer = [IntPtr]::Zero
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $expiresAt = [DateTimeOffset]::UtcNow.AddSeconds($ExpiresInSeconds).ToUnixTimeSeconds()
    $bodyHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bodyBytes))
    $message = "$expiresAt`n$Scope`n$Method`n$Path`n$bodyHash"
    $signature = [Security.Cryptography.HMACSHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($key),
        [Text.Encoding]::UTF8.GetBytes($message))
    "$expiresAt.$([Convert]::ToBase64String($signature))"
}
finally {
    $key = $null
    if ($pointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}
