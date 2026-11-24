# Load test evidence â€“ Standard 150 sessions x10

Generated 2026-11-23 14:16:42 +05:30 by `scripts/run-load-test.ps1`.

> Simulated playback sessions measured locally against the Test Media Server (authorised test environment). These figures are engine telemetry, NOT views or streams reported by YouTube or Spotify.

**Result: PASS**

## Configuration

| Setting | Value |
|---|---|
| Run id | `01a20795-d844-75bb-bb9e-461008244970` |
| Sessions | 150 |
| Max concurrency | 150 |
| Ramp-up | 5 s |
| Playback speed | x10 |
| Simulated failure rate | 5 % |
| Media | Test Media 01 â€“ 60 s clip |

## Acceptance checks

| Check | Expected | Actual | Result |
|---|---|---|---|
| Run completed | Completed | Completed | PASS |
| All sessions started | 150 | 150 | PASS |
| All sessions accounted for (terminal) | 150 | 150 | PASS |
| Engine peak concurrency >= 100 | >= 100 | 148 | PASS |
| Peak from DB timestamps >= 100 | >= 100 | 148 | PASS |
| Media server peak streams >= 100 | >= 100 | 150 | PASS |
| Engine bytes received <= bytes served by media server | <= served | 139296000 / 139296000 | PASS |

## Results

| Metric | Value |
|---|---|
| Started / completed / failed / cancelled / interrupted | 150 / 139 / 11 / 0 / 0 |
| Success rate (completed / started) | 92.67 % |
| Peak concurrency (engine, live counter) | 148 |
| Peak concurrency (reconstructed from persisted timestamps) | 148 |
| Peak active streams (measured independently by the media server) | 150 |
| Run duration | 11.1 s |
| Data received | 132.8 MB in 4353 segments |
| Aggregate throughput | 100.4 Mbps |
| Retries (sessions retried) | 71 (41) |
| Media server: transient faults / broken-stream faults injected | 38 / 44 |
| Session wall time p50 / p95 (completed) | 6 s / 6.2 s |
| Startup latency p50 / p95 | 40 ms / 58 ms |
| API peak working set | 225.2 MB |
| API max thread-pool threads | 7 |

### Errors by code

| Error code | Sessions |
|---|---|
| `STREAM_BROKEN` | 11 |

## Environment

| | |
|---|---|
| CPU | 11th Gen Intel(R) Core(TM) i3-1115G4 @ 3.00GHz (2 cores / 4 threads) |
| Memory | 15.7 GB |
| OS | Microsoft Windows 11 Home Single Language 10.0.26200 |
| .NET SDK | 10.0.403 |
| Topology | API, Test Media Server, PostgreSQL and the load generator all run on this single machine. |

## Files

`request.json` (what was sent) Â· `run.json` Â· `report.json` Â· `report.csv` (one row per session) Â·
`samples.csv` (1 Hz live counters + API memory/threads) Â· `media-server-stats.json` Â· `api-system-stats.json` Â· `environment.json`