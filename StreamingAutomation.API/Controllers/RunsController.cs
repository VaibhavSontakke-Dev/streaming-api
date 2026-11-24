using System.Text;
using Microsoft.AspNetCore.Mvc;
using StreamingAutomation.Application.Common.Models;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Application.Reports;
using StreamingAutomation.Application.Runs;
using StreamingAutomation.Domain.Enums;

namespace StreamingAutomation.API.Controllers;

/// <summary>Create, monitor, cancel and report on test runs and their playback sessions.</summary>
[ApiController]
[Route("api/runs")]
[Produces("application/json")]
public sealed class RunsController(TestRunService commands, RunQueries queries) : ControllerBase
{
    /// <summary>Lists test runs, newest first.</summary>
    /// <param name="status">Optional run status filter.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1–500).</param>
    /// <param name="cancellationToken">Request abort token.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<RunDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<RunDto>> List([FromQuery] RunStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        queries.ListRunsAsync(new RunQuery(status, page, pageSize), cancellationToken);

    /// <summary>Creates a test run and immediately starts its playback sessions.</summary>
    /// <response code="201">Run created and started.</response>
    /// <response code="400">Validation failed (e.g. session count out of range).</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<RunDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RunDto>> Create([FromBody] CreateRunRequest request, CancellationToken cancellationToken)
    {
        var runId = await commands.CreateAndStartAsync(request, cancellationToken);
        var run = await queries.GetRunAsync(runId, cancellationToken);
        return CreatedAtAction(nameof(Get), new { runId }, run);
    }

    /// <summary>Gets one run with live counters while it is executing.</summary>
    [HttpGet("{runId:guid}")]
    [ProducesResponseType<RunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<RunDto> Get(Guid runId, CancellationToken cancellationToken) => queries.GetRunAsync(runId, cancellationToken);

    /// <summary>Requests cancellation of a running test run (all active and pending sessions).</summary>
    /// <response code="202">Cancellation requested; the run finishes asynchronously.</response>
    /// <response code="404">Run not found.</response>
    /// <response code="409">Run is not running.</response>
    [HttpPost("{runId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken cancellationToken)
    {
        await commands.CancelAsync(runId, cancellationToken);
        return Accepted();
    }

    /// <summary>Lists a run's sessions with filtering, sorting and paging.</summary>
    /// <param name="runId">Run id.</param>
    /// <param name="status">Comma-separated statuses (e.g. <c>Playing,Buffering</c>) or the groups <c>active</c> / <c>terminal</c>.</param>
    /// <param name="errorCode">Exact error code, e.g. <c>STREAM_BROKEN</c>.</param>
    /// <param name="sortBy">sequence | status | progress | startedAt | endedAt | bytes | retries.</param>
    /// <param name="desc">Sort descending.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1–500).</param>
    /// <param name="cancellationToken">Request abort token.</param>
    [HttpGet("{runId:guid}/sessions")]
    [ProducesResponseType<PagedResult<SessionSnapshot>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<SessionSnapshot>> ListSessions(
        Guid runId,
        [FromQuery] string? status,
        [FromQuery] string? errorCode,
        [FromQuery] string sortBy = "sequence",
        [FromQuery] bool desc = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        queries.ListSessionsAsync(runId, new SessionQuery(status, errorCode, sortBy, desc, page, pageSize), cancellationToken);

    /// <summary>Gets one session.</summary>
    [HttpGet("{runId:guid}/sessions/{sessionId:guid}")]
    [ProducesResponseType<SessionSnapshot>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<SessionSnapshot> GetSession(Guid runId, Guid sessionId, CancellationToken cancellationToken) =>
        queries.GetSessionAsync(runId, sessionId, cancellationToken);

    /// <summary>Status-change timeline (audit trail) of one session.</summary>
    [HttpGet("{runId:guid}/sessions/{sessionId:guid}/events")]
    [ProducesResponseType<IReadOnlyList<SessionEventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<SessionEventDto>> GetSessionEvents(Guid runId, Guid sessionId, CancellationToken cancellationToken) =>
        queries.GetSessionEventsAsync(runId, sessionId, cancellationToken);

    /// <summary>Requests cancellation of a single session.</summary>
    /// <response code="202">Cancellation requested.</response>
    /// <response code="404">Run or session not found.</response>
    /// <response code="409">Session already finished.</response>
    [HttpPost("{runId:guid}/sessions/{sessionId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelSession(Guid runId, Guid sessionId, CancellationToken cancellationToken)
    {
        await commands.CancelSessionAsync(runId, sessionId, cancellationToken);
        return Accepted();
    }

    /// <summary>Results report: counts, success rate, latency percentiles, errors and concurrency timeline.</summary>
    /// <param name="runId">Run id.</param>
    /// <param name="format"><c>json</c> (default) or <c>csv</c> (summary block + one row per session).</param>
    /// <param name="reports">Report service.</param>
    /// <param name="cancellationToken">Request abort token.</param>
    [HttpGet("{runId:guid}/report")]
    [Produces("application/json", "text/csv")]
    [ProducesResponseType<RunReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Report(Guid runId, [FromServices] ReportService reports, [FromQuery] string? format = null, CancellationToken cancellationToken = default)
    {
        var (report, sessions) = await reports.BuildAsync(runId, cancellationToken);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = CsvReportWriter.Write(report, sessions);
            var fileName = $"run-report-{report.Run.CreatedAt:yyyyMMdd-HHmmss}-{runId.ToString()[..8]}.csv";
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", fileName);
        }

        return Ok(report);
    }
}
