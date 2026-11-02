param([Parameter(Mandatory)][string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$summary = @(Get-ChildItem $ResultsDirectory -Recurse -Filter '*.trx' | ForEach-Object {
    [xml]$trx = Get-Content $_.FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    [ordered]@{
        file = $_.Name
        total = [int]$counters.total
        passed = [int]$counters.passed
        failed = [int]$counters.failed
        skipped = [int]$counters.notExecuted
        skippedTests = @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'NotExecuted' | ForEach-Object {
            [ordered]@{ name = $_.testName; reason = [string]$_.Output.ErrorInfo.Message }
        })
    }
})
$summary | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ResultsDirectory 'summary.json')
foreach ($suite in $summary) {
    $line = "$($suite.file): $($suite.passed) passed, $($suite.failed) failed, $($suite.skipped) skipped, $($suite.total) total"
    Write-Output $line
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $line }
}
