# Load test evidence â€“ Stress 500 sessions x5

Generated 2026-11-23 14:18:19 +05:30 by `scripts/run-load-test.ps1`.

> Simulated playback sessions measured locally against the Test Media Server (authorised test environment). These figures are engine telemetry, NOT views or streams reported by YouTube or Spotify.

**Result: PASS**

## Configuration

| Setting | Value |
|---|---|
| Run id | `01a20797-3a2f-74e6-bffe-636790d10e6b` |
| Sessions | 500 |
| Max concurrency | 500 |
| Ramp-up | 5 s |
| Playback speed | x5 |
| Simulated failure rate | 5 % |
| Media | Test Media 01 â€“ 60 s clip |

## Acceptance checks

| Check | Expected | Actual | Result |
|---|---|---|---|
| Run completed | Completed | Completed | PASS |
| All sessions started | 500 | 500 | PASS |
| All sessions accounted for (terminal) | 500 | 500 | PASS |
| Engine peak concurrency >= 100 | >= 100 | 496 | PASS |
| Peak from DB timestamps >= 100 | >= 100 | 496 | PASS |
| Media server peak streams >= 100 | >= 100 | 500 | PASS |
| Engine bytes received <= bytes served by media server | <= served | 466784000 / 466784000 | PASS |

## Results

| Metric | Value |
|---|---|
| Started / completed / failed / cancelled / interrupted | 500 / 477 / 23 / 0 / 0 |
| Success rate (completed / started) | 95.4 % |
| Peak concurrency (engine, live counter) | 496 |
| Peak concurrency (reconstructed from persisted timestamps) | 496 |
| Peak active streams (measured independently by the media server) | 500 |
| Run duration | 17.1 s |
| Data received | 445.2 MB in 14587 segments |
| Aggregate throughput | 218.4 Mbps |
| Retries (sessions retried) | 207 (143) |
| Media server: transient faults / broken-stream faults injected | 138 / 92 |
| Session wall time p50 / p95 (completed) | 11.9 s / 11.9 s |
| Startup latency p50 / p95 | 41 ms / 59 ms |
| API peak working set | 222.5 MB |
| API max thread-pool threads | 6 |

### Errors by code

| Error code | Sessions |
|---|---|
| `STREAM_BROKEN` | 23 |

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