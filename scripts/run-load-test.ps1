<#
.SYNOPSIS
    Repeatable concurrency verification for the Streaming Automation prototype.

.DESCRIPTION
    1. Checks that the API and the Test Media Server are healthy.
    2. Resets the media server's independent stream telemetry.
    3. Starts a test run through the public REST API.
    4. Samples live engine counters + process telemetry once per second while the run executes.
    5. Downloads the final run, the JSON and CSV reports and the media server's own statistics.
    6. Asserts the acceptance criteria (all sessions accounted for, peak concurrency >= target,
       measured both by the engine and independently by the media server).
    7. Writes everything to docs/evidence/<timestamp>-<sessions>-sessions/ and a summary.md.

    Exit code 0 = all checks passed, 1 = a check failed, 2 = environment problem.

.EXAMPLE
    .\scripts\run-load-test.ps1                                  # 150 sessions, x10 speed, 5% faults
.EXAMPLE
    .\scripts\run-load-test.ps1 -Sessions 500 -PlaybackSpeed 20  # stress
.EXAMPLE
    .\scripts\run-load-test.ps1 -Sessions 100 -PlaybackSpeed 1 -FailureRate 0 -AllMedia   # real time, full catalogue
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 1000)] [int] $Sessions = 150,
    [ValidateRange(0, 1000)] [int] $MaxConcurrency = 0,          # 0 = same as -Sessions
    [ValidateRange(0, 3600)] [int] $RampUpSeconds = 5,
    [ValidateRange(0.1, 100)] [double] $PlaybackSpeed = 10,
    [ValidateRange(0.0, 1.0)] [double] $FailureRate = 0.05,
    [switch] $AllMedia,                                           # default: 60 s short clip only
    [int] $TargetConcurrency = 100,
    [string] $Name = '',
    [string] $ApiUrl = 'http://localhost:5126',
    [string] $MediaServerUrl = 'http://localhost:5200',
    [string] $OutputRoot = '',                                    # default: <repo>/docs/evidence
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ShortClipId = '8f1c2a10-0000-4000-8000-000000000001'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Step([string] $message) { Write-Host "==> $message" -ForegroundColor Cyan }

function Save-Json([object] $value, [string] $path) {
    [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 12), $Utf8NoBom)
}

function Test-Endpoint([string] $url) {
    try { return (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { return $false }
}

# ---------------------------------------------------------------- 1. environment
Write-Step "Checking services"
if (-not (Test-Endpoint "$ApiUrl/health")) { Write-Error "API not healthy at $ApiUrl/health. Start it first (see README)."; exit 2 }
if (-not (Test-Endpoint "$MediaServerUrl/health")) { Write-Error "Test Media Server not healthy at $MediaServerUrl/health."; exit 2 }

if ($MaxConcurrency -eq 0) { $MaxConcurrency = $Sessions }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\docs\evidence' }
if ([string]::IsNullOrWhiteSpace($Name)) { $Name = "Load test $Sessions x$PlaybackSpeed ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $OutputRoot "$stamp-$Sessions-sessions"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDir = (Resolve-Path $outDir).Path

$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$machine = [ordered]@{
    cpu              = $cpu.Name.Trim()
    physicalCores    = $cpu.NumberOfCores
    logicalCores     = $cpu.NumberOfLogicalProcessors
    memoryGb         = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    operatingSystem  = "$($os.Caption) $($os.Version)"
    dotnetSdk        = (& dotnet --version)
    note             = 'API, Test Media Server, PostgreSQL and the load generator all run on this single machine.'
}
Save-Json $machine (Join-Path $outDir 'environment.json')

# ---------------------------------------------------------------- 2. reset telemetry
Write-Step "Resetting media server telemetry"
Invoke-RestMethod -Method Post -Uri "$MediaServerUrl/stats/reset" | Out-Null

# ---------------------------------------------------------------- 3. start run
$body = [ordered]@{
    name                 = $Name
    sessionCount         = $Sessions
    maxConcurrency       = $MaxConcurrency
    rampUpSeconds        = $RampUpSeconds
    playbackSpeed        = $PlaybackSpeed
    simulatedFailureRate = $FailureRate
}
if (-not $AllMedia) { $body.mediaItemIds = @($ShortClipId) }
Save-Json $body (Join-Path $outDir 'request.json')

Write-Step "Starting run: $Sessions sessions, concurrency $MaxConcurrency, ramp-up ${RampUpSeconds}s, speed x$PlaybackSpeed, failure rate $FailureRate"
$run = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/runs" -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 5)
$runId = $run.id
Write-Host "    run id: $runId"

# ---------------------------------------------------------------- 4. sample while running
$samples = New-Object System.Collections.Generic.List[object]
$clock = [Diagnostics.Stopwatch]::StartNew()
do {
    Start-Sleep -Milliseconds 1000
    $run = Invoke-RestMethod -Uri "$ApiUrl/api/runs/$runId"
    $sys = Invoke-RestMethod -Uri "$ApiUrl/api/system/stats"
    $media = Invoke-RestMethod -Uri "$MediaServerUrl/stats"
    $samples.Add([pscustomobject]@{
        elapsed_s          = [math]::Round($clock.Elapsed.TotalSeconds, 1)
        status             = $run.status
        active             = $run.active
        pending            = $run.pending
        started            = $run.started
        completed          = $run.completed
        failed             = $run.failed
        media_active_streams = $media.activeStreams
        working_set_mb     = $sys.workingSetMb
        threadpool_threads = $sys.threadPoolThreads
        cpu_time_s         = $sys.cpuTimeSeconds
    })
    Write-Progress -Activity "Run $runId" -Status ("active {0}  completed {1}  failed {2}  pending {3}" -f $run.active, $run.completed, $run.failed, $run.pending) `
        -PercentComplete ([math]::Min(100, 100 * ($run.completed + $run.failed + $run.cancelled + $run.interrupted) / $Sessions))
    if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) { Write-Error "Timed out after $TimeoutSeconds s"; exit 1 }
} while ($run.status -eq 'Running' -or $run.status -eq 'Pending')
Write-Progress -Activity "Run $runId" -Completed
$samples | Export-Csv -NoTypeInformation -Path (Join-Path $outDir 'samples.csv')

# ---------------------------------------------------------------- 5. collect results
Write-Step "Collecting results"
$run = Invoke-RestMethod -Uri "$ApiUrl/api/runs/$runId"
$report = Invoke-RestMethod -Uri "$ApiUrl/api/runs/$runId/report"
$mediaStats = Invoke-RestMethod -Uri "$MediaServerUrl/stats"
$systemStats = Invoke-RestMethod -Uri "$ApiUrl/api/system/stats"
Save-Json $run (Join-Path $outDir 'run.json')
Save-Json $report (Join-Path $outDir 'report.json')
Save-Json $mediaStats (Join-Path $outDir 'media-server-stats.json')
Save-Json $systemStats (Join-Path $outDir 'api-system-stats.json')
Invoke-WebRequest -UseBasicParsing -Uri "$ApiUrl/api/runs/$runId/report?format=csv" -OutFile (Join-Path $outDir 'report.csv')

# ---------------------------------------------------------------- 6. assertions
$s = $report.summary
$accounted = $s.completed + $s.failed + $s.cancelled + $s.interrupted
$peakWorkingSet = ($samples | Measure-Object working_set_mb -Maximum).Maximum
$maxThreads = ($samples | Measure-Object threadpool_threads -Maximum).Maximum
$checks = @(
    [pscustomobject]@{ check = 'Run completed';                                   expected = 'Completed';          actual = $run.status;                       passed = ($run.status -eq 'Completed') }
    [pscustomobject]@{ check = 'All sessions started';                            expected = $Sessions;            actual = $s.started;                        passed = ($s.started -eq $Sessions) }
    [pscustomobject]@{ check = 'All sessions accounted for (terminal)';           expected = $Sessions;            actual = $accounted;                        passed = ($accounted -eq $Sessions) }
    [pscustomobject]@{ check = "Engine peak concurrency >= $TargetConcurrency";   expected = ">= $TargetConcurrency"; actual = $s.peakConcurrency;          passed = ($s.peakConcurrency -ge $TargetConcurrency) }
    [pscustomobject]@{ check = "Peak from DB timestamps >= $TargetConcurrency";   expected = ">= $TargetConcurrency"; actual = $s.observedPeakConcurrency;  passed = ($s.observedPeakConcurrency -ge $TargetConcurrency) }
    [pscustomobject]@{ check = "Media server peak streams >= $TargetConcurrency"; expected = ">= $TargetConcurrency"; actual = $mediaStats.peakActiveStreams; passed = ($mediaStats.peakActiveStreams -ge $TargetConcurrency) }
    [pscustomobject]@{ check = 'Engine bytes received <= bytes served by media server'; expected = '<= served'; actual = "$($s.totalBytesReceived) / $($mediaStats.bytesServed)"; passed = ($s.totalBytesReceived -le $mediaStats.bytesServed -and $s.totalBytesReceived -gt 0) }
)
$allPassed = -not ($checks | Where-Object { -not $_.passed })

# ---------------------------------------------------------------- 7. summary
$rows = ($checks | ForEach-Object { "| $($_.check) | $($_.expected) | $($_.actual) | $(if ($_.passed) { 'PASS' } else { 'FAIL' }) |" }) -join "`n"
$errors = if (@($report.errors).Count -gt 0) { ($report.errors | ForEach-Object { "| ``$($_.errorCode)`` | $($_.count) |" }) -join "`n" } else { '| (none) | 0 |' }
$summary = @"
# Load test evidence – $Name

Generated $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') by ``scripts/run-load-test.ps1``.

> $($report.dataSource)

**Result: $(if ($allPassed) { 'PASS' } else { 'FAIL' })**

## Configuration

| Setting | Value |
|---|---|
| Run id | ``$runId`` |
| Sessions | $Sessions |
| Max concurrency | $MaxConcurrency |
| Ramp-up | $RampUpSeconds s |
| Playback speed | x$PlaybackSpeed |
| Simulated failure rate | $([math]::Round($FailureRate * 100, 1)) % |
| Media | $(if ($AllMedia) { 'whole catalogue (round-robin)' } else { 'Test Media 01 – 60 s clip' }) |

## Acceptance checks

| Check | Expected | Actual | Result |
|---|---|---|---|
$rows

## Results

| Metric | Value |
|---|---|
| Started / completed / failed / cancelled / interrupted | $($s.started) / $($s.completed) / $($s.failed) / $($s.cancelled) / $($s.interrupted) |
| Success rate (completed / started) | $($s.successRatePercent) % |
| Peak concurrency (engine, live counter) | $($s.peakConcurrency) |
| Peak concurrency (reconstructed from persisted timestamps) | $($s.observedPeakConcurrency) |
| Peak active streams (measured independently by the media server) | $($mediaStats.peakActiveStreams) |
| Run duration | $($s.runDurationSeconds) s |
| Data received | $([math]::Round($s.totalBytesReceived / 1MB, 1)) MB in $($s.totalSegments) segments |
| Aggregate throughput | $([math]::Round($s.throughputKbps / 1000, 1)) Mbps |
| Retries (sessions retried) | $($s.totalRetries) ($($s.sessionsWithRetries)) |
| Media server: transient faults / broken-stream faults injected | $($mediaStats.transientErrorsInjected) / $($mediaStats.brokenStreamErrorsInjected) |
| Session wall time p50 / p95 (completed) | $($report.sessionDurationSeconds.p50) s / $($report.sessionDurationSeconds.p95) s |
| Startup latency p50 / p95 | $($report.startupLatencyMs.p50) ms / $($report.startupLatencyMs.p95) ms |
| API peak working set | $peakWorkingSet MB |
| API max thread-pool threads | $maxThreads |

### Errors by code

| Error code | Sessions |
|---|---|
$errors

## Environment

| | |
|---|---|
| CPU | $($machine.cpu) ($($machine.physicalCores) cores / $($machine.logicalCores) threads) |
| Memory | $($machine.memoryGb) GB |
| OS | $($machine.operatingSystem) |
| .NET SDK | $($machine.dotnetSdk) |
| Topology | $($machine.note) |

## Files

``request.json`` (what was sent) · ``run.json`` · ``report.json`` · ``report.csv`` (one row per session) ·
``samples.csv`` (1 Hz live counters + API memory/threads) · ``media-server-stats.json`` · ``api-system-stats.json`` · ``environment.json``
"@
[IO.File]::WriteAllText((Join-Path $outDir 'summary.md'), $summary, $Utf8NoBom)

Write-Host ""
$checks | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("Evidence written to {0}" -f $outDir)
if ($allPassed) { Write-Host "RESULT: PASS" -ForegroundColor Green; exit 0 }
Write-Host "RESULT: FAIL" -ForegroundColor Red
exit 1
