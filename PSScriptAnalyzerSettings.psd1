@{
    # Use the default PSScriptAnalyzer rule set as a baseline for this repository.
    IncludeDefaultRules = $true

    Severity             = @('Error', 'Warning')

    ExcludeRules = @(
        # This repo's PowerShell usage is limited to CI workflow snippets; formatting-only
        # rules add noise without improving reliability.
        'PSAvoidTrailingWhitespace',
        'PSUseConsistentWhitespace',
        'PSUseConsistentIndentation'
    )
}
