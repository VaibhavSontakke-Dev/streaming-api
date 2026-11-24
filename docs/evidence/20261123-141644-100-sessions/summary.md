# Load test evidence â€“ Real-time 100 sessions x1

Generated 2026-11-23 14:17:55 +05:30 by `scripts/run-load-test.ps1`.

> Simulated playback sessions measured locally against the Test Media Server (authorised test environment). These figures are engine telemetry, NOT views or streams reported by YouTube or Spotify.

**Result: PASS**

## Configuration

| Setting | Value |
|---|---|
| Run id | `01a20796-17ca-7149-ae3e-102ca75aac6f` |
| Sessions | 100 |
| Max concurrency | 100 |
| Ramp-up | 10 s |
| Playback speed | x1 |
| Simulated failure rate | 2 % |
| Media | Test Media 01 â€“ 60 s clip |

## Acceptance checks

| Check | Expected | Actual | Result |
|---|---|---|---|
| Run completed | Completed | Completed | PASS |
| All sessions started | 100 | 100 | PASS |
| All sessions accounted for (terminal) | 100 | 100 | PASS |
| Engine peak concurrency >= 100 | >= 100 | 100 | PASS |
| Peak from DB timestamps >= 100 | >= 100 | 100 | PASS |
| Media server peak streams >= 100 | >= 100 | 100 | PASS |
| Engine bytes received <= bytes served by media server | <= served | 95552000 / 95552000 | PASS |

## Results

| Metric | Value |
|---|---|
| Started / completed / failed / cancelled / interrupted | 100 / 99 / 1 / 0 / 0 |
| Success rate (completed / started) | 99 % |
| Peak concurrency (engine, live counter) | 100 |
| Peak concurrency (reconstructed from persisted timestamps) | 100 |
| Peak active streams (measured independently by the media server) | 100 |
| Run duration | 68.3 s |
| Data received | 91.1 MB in 2986 segments |
| Aggregate throughput | 11.2 Mbps |
| Retries (sessions retried) | 45 (38) |
| Media server: transient faults / broken-stream faults injected | 42 / 4 |
| Session wall time p50 / p95 (completed) | 58.2 s / 58.3 s |
| Startup latency p50 / p95 | 41 ms / 58 ms |
| API peak working set | 227 MB |
| API max thread-pool threads | 7 |

### Errors by code

| Error code | Sessions |
|---|---|
| `STREAM_BROKEN` | 1 |

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