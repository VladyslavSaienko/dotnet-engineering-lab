<#
.SYNOPSIS
Runs the experiment 2.1 k6 load scenarios against one running variant and writes ADR 0003 results.

.DESCRIPTION
The script does not start the variant. Start it first, in Release and without a debugger, for example:

    dotnet run -c Release --project experiments/2.1-last-copy/src/LastCopy.AppHost

For every scenario and run it writes:
    results/<Variant>/<Scenario>/<timestamp>.json   ADR 0003 result (committed)
    results/<Variant>/<Scenario>/<timestamp>.html   k6 web dashboard report (committed)
    results/raw/<Variant>/<Scenario>/<timestamp>.*  raw k6 output (git-ignored)

Every result file is validated against load/result.schema.json. At the end the script prints the median of the runs.
See load/README.md for prerequisites and measurement caveats.

.EXAMPLE
.\run.ps1 -Variant V0

.EXAMPLE
.\run.ps1 -Variant V0 -Scenario LS-2.1-01 -Runs 1 -SkipWarmUp
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^V[0-9]+$')]
    [string] $Variant,

    [ValidateSet('LS-2.1-01', 'LS-2.1-02', 'LS-2.1-03', 'all')]
    [string] $Scenario = 'all',

    [string] $BaseUrl = 'http://localhost:53643',

    [ValidateRange(1, 100)]
    [int] $Runs = 3,

    # Pause between runs so the variant and SQL Server settle (connections, background work).
    [ValidateRange(0, 600)]
    [int] $CooldownSeconds = 10,

    [switch] $SkipWarmUp,

    # Also stream every k6 sample to results/raw (large, and adds load on the machine being measured).
    [switch] $RawSamples
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Scripts = [ordered]@{
    'LS-2.1-01' = 'ls-2.1-01-hot-item.js'
    'LS-2.1-02' = 'ls-2.1-02-sustained.js'
    'LS-2.1-03' = 'ls-2.1-03-spike.js'
}

$LoadDir = $PSScriptRoot
$ExperimentDir = Split-Path -Parent $LoadDir
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ExperimentDir)
$ResultsDir = Join-Path $ExperimentDir 'results'
$RawDir = Join-Path $ResultsDir 'raw'
$BaseUrl = $BaseUrl.TrimEnd('/')

function Write-Utf8File([string] $Path, [string] $Content) {
    # Windows PowerShell 5.1 writes a BOM with -Encoding UTF8, which breaks JSON.parse in k6.
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-NativeOutput([scriptblock] $Command) {
    # Missing tools (e.g. Docker not running) must not stop the run; the value is recorded as 'unknown'.
    $ErrorActionPreference = 'Continue'
    try { (& $Command 2>$null) -join "`n" } catch { '' }
}

function Assert-Tool([string] $Name, [string] $Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found on PATH. $Hint"
    }
}

function Test-Variant {
    try {
        $null = Invoke-WebRequest -Uri "$BaseUrl/test-support/summary" -UseBasicParsing -TimeoutSec 5
        return $true
    }
    catch {
        Write-Warning "The variant is not reachable at $BaseUrl/test-support/summary ($($_.Exception.Message))."
        Write-Warning 'This script does not start the variant. Start it in Release and in the Development environment, e.g.:'
        Write-Warning '  dotnet run -c Release --project experiments/2.1-last-copy/src/LastCopy.AppHost'
        Write-Warning 'then pass the endpoint shown in the Aspire dashboard with -BaseUrl.'
        return $false
    }
}

function Test-ReleaseBuild {
    # Best effort: look for the variant's process and check that it runs from bin\Release.
    $pattern = "*LastCopy.$Variant.*"
    $processes = @(Get-CimInstance Win32_Process |
        Where-Object { ($_.ExecutablePath -like $pattern) -or ($_.CommandLine -like $pattern) })
    if ($processes.Count -eq 0) {
        Write-Warning "No local LastCopy.$Variant.* process found, so the build configuration cannot be checked."
        return
    }
    # The AppHost starts variants with "dotnet run --configuration <its own configuration>".
    $debug = @($processes | Where-Object {
            $line = "$($_.ExecutablePath) $($_.CommandLine)"
            ($line -like '*\bin\Debug\*') -or ($line -like '*--configuration Debug*') -or ($line -like '*-c Debug*')
        })
    if ($debug.Count -gt 0) {
        Write-Warning "LastCopy.$Variant runs from bin\Debug. Results are not comparable; restart it with -c Release."
    }
}

function Get-MeasurementEnvironment {
    $os = Get-CimInstance Win32_OperatingSystem
    $processors = @(Get-CimInstance Win32_Processor)
    $computer = Get-CimInstance Win32_ComputerSystem

    $cpu = ($processors[0].Name -replace '\s+', ' ').Trim()
    $ramGb = [math]::Round($computer.TotalPhysicalMemory / 1GB, 1)

    # The variant targets net<major>.0 and rolls forward to the newest installed patch of that runtime.
    $major = [regex]::Match((Get-Content (Join-Path $RepoRoot 'Directory.Build.props') -Raw), '<TargetFramework>net(\d+)\.').Groups[1].Value
    $runtimes = (Get-NativeOutput { dotnet --list-runtimes }) -split "`n" |
        ForEach-Object { if ($_ -match '^Microsoft\.AspNetCore\.App (\d+\.\d+\.\d+) ') { [version] $Matches[1] } } |
        Where-Object { $_.Major -eq [int] $major } |
        Sort-Object -Descending
    $dotnet = if ($runtimes) { "$(@($runtimes)[0])" } else { 'unknown' }

    $sqlImage = (Get-NativeOutput { docker ps --format '{{.Image}}' }) -split "`n" |
        Where-Object { $_ -match 'mssql' } | Select-Object -First 1
    if ($sqlImage) {
        # Tags such as 2022-latest move; the digest identifies the exact image (recorded as tag@sha256:...).
        $repoDigest = Get-NativeOutput { docker image inspect --format '{{index .RepoDigests 0}}' $sqlImage }
        if ($repoDigest -match '@(sha256:[0-9a-f]+)') { $sqlImage = "$sqlImage@$($Matches[1])" }
    }
    $dockerMemory = Get-NativeOutput { docker info --format '{{.MemTotal}}' }
    $k6 = [regex]::Match((Get-NativeOutput { k6 version }), 'v(\d+\.\d+\.\d+)').Groups[1].Value

    [ordered]@{
        machine           = "$cpu, $ramGb GB"
        os                = "$($os.Caption.Trim()) $($os.Version)"
        cpu               = $cpu
        cores             = [int] ($processors | Measure-Object -Property NumberOfCores -Sum).Sum
        logicalProcessors = [int] ($processors | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum
        ramGb             = $ramGb
        dockerMemoryGb    = if ($dockerMemory -match '^\d+$') { [math]::Round([double] $dockerMemory / 1GB, 1) } else { $null }
        dotnet            = $dotnet
        sqlServerImage    = if ($sqlImage) { $sqlImage } else { 'unknown' }
        k6                = if ($k6) { $k6 } else { 'unknown' }
    }
}

function Invoke-K6([string] $Script, [hashtable] $Environment, [string[]] $ExtraArgs = @()) {
    # k6 logs warnings (e.g. failed requests) to stderr. When the caller redirects stderr, Windows PowerShell
    # turns each line into an error record, which must not stop the run; the exit code decides.
    $ErrorActionPreference = 'Continue'
    foreach ($name in $Environment.Keys) { Set-Item "env:$name" $Environment[$name] }
    try {
        # Out-Host keeps k6 output out of the function result, which must be only the exit code.
        & k6 run @ExtraArgs (Join-Path $LoadDir $Script) | Out-Host
        return $LASTEXITCODE
    }
    finally {
        foreach ($name in $Environment.Keys) { Remove-Item "env:$name" -ErrorAction SilentlyContinue }
    }
}

function Test-ResultFiles([string[]] $Files) {
    & node (Join-Path $LoadDir 'tools\validate-results.mjs') @Files | Out-Host
    return $LASTEXITCODE -eq 0
}

function Get-Median([double[]] $Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

# --- Preconditions ---------------------------------------------------------------------------------

Assert-Tool 'k6' 'Install it from https://grafana.com/docs/k6/latest/set-up/install-k6/.'
Assert-Tool 'node' 'Node.js 18+ is needed to validate result files against the JSON schema.'

if (-not (Test-Path (Join-Path $LoadDir 'node_modules'))) {
    Write-Host 'Installing the schema validator (npm ci)...'
    & npm ci --prefix $LoadDir --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
}

if (-not (Test-Variant)) { exit 1 }
Test-ReleaseBuild

$selected = if ($Scenario -eq 'all') { @($Scripts.Keys) } else { @($Scenario) }
$batchStamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd'T'HH-mm-ss'Z'")

$environmentDir = Join-Path $RawDir $Variant
New-Item -ItemType Directory -Force $environmentDir | Out-Null
$environmentFile = Join-Path $environmentDir "environment-$batchStamp.json"
Write-Utf8File $environmentFile (Get-MeasurementEnvironment | ConvertTo-Json -Depth 5)
Write-Host "Measurement environment: $environmentFile"

$common = @{ BASE_URL = $BaseUrl; VARIANT = $Variant }

if (-not $SkipWarmUp) {
    Write-Host "`n=== Warm-up (not recorded) ===" -ForegroundColor Cyan
    $exitCode = Invoke-K6 'warmup.js' $common @('--quiet')
    if ($exitCode -ne 0) { throw "Warm-up failed with k6 exit code $exitCode." }
}

# --- Measured runs ---------------------------------------------------------------------------------

$produced = New-Object System.Collections.Generic.List[string]
$failures = 0
$isFirstRun = $true

foreach ($scenarioId in $selected) {
    for ($run = 1; $run -le $Runs; $run++) {
        if (-not $isFirstRun -and $CooldownSeconds -gt 0) { Start-Sleep -Seconds $CooldownSeconds }
        $isFirstRun = $false

        $now = (Get-Date).ToUniversalTime()
        $fileStamp = $now.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'")
        $resultDir = Join-Path $ResultsDir "$Variant\$scenarioId"
        $rawScenarioDir = Join-Path $RawDir "$Variant\$scenarioId"
        New-Item -ItemType Directory -Force $resultDir, $rawScenarioDir | Out-Null

        $resultFile = Join-Path $resultDir "$fileStamp.json"
        $reportFile = Join-Path $resultDir "$fileStamp.html"
        $rawPrefix = Join-Path $rawScenarioDir $fileStamp

        Write-Host "`n=== $Variant $scenarioId run $run of $Runs ===" -ForegroundColor Cyan

        $environment = $common.Clone()
        $environment += @{
            RUN                     = "$run"
            RUN_TIMESTAMP_UTC       = $now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
            ENVIRONMENT_FILE        = $environmentFile
            RESULT_FILE             = $resultFile
            RAW_PREFIX              = $rawPrefix
            K6_WEB_DASHBOARD        = 'true'
            K6_WEB_DASHBOARD_EXPORT = $reportFile
            # The hot-item scenario lasts about a second; the default 10 s period would leave the report empty.
            K6_WEB_DASHBOARD_PERIOD = '1s'
        }
        $extraArgs = if ($RawSamples) { @('--out', "json=$rawPrefix.samples.json.gz") } else { @() }

        $exitCode = Invoke-K6 $Scripts[$scenarioId] $environment $extraArgs

        if ($exitCode -eq 99) {
            # Thresholds only cover the error rate; the run is still a valid measurement and is kept.
            Write-Warning "$scenarioId run ${run}: error-rate threshold crossed (k6 exit code 99). Result kept."
        }
        elseif ($exitCode -ne 0) {
            Write-Warning "$scenarioId run ${run}: k6 failed with exit code $exitCode. Result discarded."
            Remove-Item $resultFile, $reportFile -ErrorAction SilentlyContinue
            $failures++
            continue
        }

        if (-not (Test-Path $resultFile)) {
            Write-Warning "$scenarioId run ${run}: k6 did not write $resultFile."
            $failures++
            continue
        }

        if (Test-ResultFiles @($resultFile)) {
            $produced.Add($resultFile)
        }
        else {
            $failures++
        }
    }
}

# --- Summary ---------------------------------------------------------------------------------------

if ($produced.Count -gt 0) {
    Write-Host "`n=== $Variant, median of runs ===" -ForegroundColor Cyan
    $results = $produced | ForEach-Object { Get-Content $_ -Raw | ConvertFrom-Json }
    $results | Group-Object scenario | ForEach-Object {
        $m = @($_.Group | ForEach-Object { $_.metrics })
        $rps = @($m | ForEach-Object { $_.requestsPerSecond })
        [pscustomobject]@{
            Scenario  = $_.Name
            Runs      = $m.Count
            RPS       = Get-Median $rps
            'RPS min-max' = '{0}-{1}' -f ($rps | Measure-Object -Minimum).Minimum, ($rps | Measure-Object -Maximum).Maximum
            'p50 ms'  = Get-Median @($m | ForEach-Object { $_.latencyMs.p50 })
            'p95 ms'  = Get-Median @($m | ForEach-Object { $_.latencyMs.p95 })
            'p99 ms'  = Get-Median @($m | ForEach-Object { $_.latencyMs.p99 })
            Succeeded = Get-Median @($m | ForEach-Object { $_.succeeded })
            Rejected  = Get-Median @($m | ForEach-Object { $_.rejected })
            Errors    = Get-Median @($m | ForEach-Object { $_.errors })
            Oversold  = Get-Median @($m | ForEach-Object { $_.oversold })
        }
    } | Format-Table -Property * -AutoSize | Out-String -Width 250 | Write-Host
}

if ($failures -gt 0) {
    Write-Warning "$failures run(s) failed or produced invalid results."
    exit 1
}
Write-Host "All $($produced.Count) result file(s) are valid."
