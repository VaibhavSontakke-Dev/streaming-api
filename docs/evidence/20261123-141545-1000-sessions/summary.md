# Load test evidence â€“ Stress 1000 sessions x5

Generated 2026-11-23 14:16:13 +05:30 by `scripts/run-load-test.ps1`.

> Simulated playback sessions measured locally against the Test Media Server (authorised test environment). These figures are engine telemetry, NOT views or streams reported by YouTube or Spotify.

**Result: PASS**

## Configuration

| Setting | Value |
|---|---|
| Run id | `01a20795-349c-7878-98c0-54e4877f1489` |
| Sessions | 1000 |
| Max concurrency | 1000 |
| Ramp-up | 10 s |
| Playback speed | x5 |
| Simulated failure rate | 5 % |
| Media | Test Media 01 â€“ 60 s clip |

## Acceptance checks

| Check | Expected | Actual | Result |
|---|---|---|---|
| Run completed | Completed | Completed | PASS |
| All sessions started | 1000 | 1000 | PASS |
| All sessions accounted for (terminal) | 1000 | 1000 | PASS |
| Engine peak concurrency >= 100 | >= 100 | 986 | PASS |
| Peak from DB timestamps >= 100 | >= 100 | 986 | PASS |
| Media server peak streams >= 100 | >= 100 | 1000 | PASS |
| Engine bytes received <= bytes served by media server | <= served | 938976000 / 938976000 | PASS |

## Results

| Metric | Value |
|---|---|
| Started / completed / failed / cancelled / interrupted | 1000 / 953 / 47 / 0 / 0 |
| Success rate (completed / started) | 95.3 % |
| Peak concurrency (engine, live counter) | 986 |
| Peak concurrency (reconstructed from persisted timestamps) | 986 |
| Peak active streams (measured independently by the media server) | 1000 |
| Run duration | 22.3 s |
| Data received | 895.5 MB in 29343 segments |
| Aggregate throughput | 336.9 Mbps |
| Retries (sessions retried) | 413 (277) |
| Media server: transient faults / broken-stream faults injected | 272 / 188 |
| Session wall time p50 / p95 (completed) | 11.9 s / 12 s |
| Startup latency p50 / p95 | 42 ms / 65 ms |
| API peak working set | 219.5 MB |
| API max thread-pool threads | 13 |

### Errors by code

| Error code | Sessions |
|---|---|
| `STREAM_BROKEN` | 47 |

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