using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamingAutomation.Application.Common.Models;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Application.Reports;
using StreamingAutomation.Application.Runs;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.IntegrationTests.Engine;

namespace StreamingAutomation.IntegrationTests.Api;

/// <summary>Black-box tests over HTTP: status codes, problem details, filters, paging, report formats.</summary>
[Collection(ApiCollection.Name)]
public class RunsApiTests(ApiFactory api)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private const string ShortClip = "8f1c2a10-0000-4000-8000-000000000001";

    private readonly HttpClient _client = api.CreateClient();

    [Fact]
    public async Task CreateRun_WithInvalidBody_Returns400_WithFieldErrorsAndTraceId()
    {
        var response = await _client.PostAsJsonAsync("/api/runs", new { name = "", sessionCount = 0, playbackSpeed = 5000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("sessionCount", out _));
        Assert.True(errors.TryGetProperty("playbackSpeed", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnknownRun_Returns404Problem()
    {
        var response = await _client.GetAsync($"/api/runs/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var cancel = await _client.PostAsync($"/api/runs/{Guid.NewGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
    }

    [Fact]
    public async Task FullFlow_Create_Monitor_Filter_Page_Report_AndConflictOnLateCancel()
    {
        // Create
        var create = await _client.PostAsJsonAsync("/api/runs", new
        {
            name = "API flow",
            sessionCount = 20,
            maxConcurrency = 20,
            playbackSpeed = 100,
            simulatedFailureRate = 0.25,
            mediaItemIds = new[] { ShortClip },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var created = (await create.Content.ReadFromJsonAsync<RunDto>(Json))!;
        Assert.Equal(RunStatus.Running, created.Status);
        Assert.Contains("\"status\":\"Running\"", await create.Content.ReadAsStringAsync(), StringComparison.Ordinal); // enums as strings

        // Monitor until done
        var run = await WaitForStatusAsync(created.Id, RunStatus.Completed);
        Assert.Equal(20, run.Started);
        Assert.Equal(20, run.Completed + run.Failed);
        Assert.False(run.IsLive);

        // Filter by status
        var failed = await GetAsync<PagedResult<SessionSnapshot>>($"/api/runs/{run.Id}/sessions?status=Failed&pageSize=100");
        Assert.Equal(run.Failed, failed.TotalCount);
        Assert.All(failed.Items, s => Assert.Equal("STREAM_BROKEN", s.ErrorCode));

        var byError = await GetAsync<PagedResult<SessionSnapshot>>($"/api/runs/{run.Id}/sessions?errorCode=stream_broken");
        Assert.Equal(run.Failed, byError.TotalCount);

        var terminal = await GetAsync<PagedResult<SessionSnapshot>>($"/api/runs/{run.Id}/sessions?status=terminal");
        Assert.Equal(20, terminal.TotalCount);

        // Paging + sorting
        var page2 = await GetAsync<PagedResult<SessionSnapshot>>($"/api/runs/{run.Id}/sessions?page=2&pageSize=5");
        Assert.Equal([6, 7, 8, 9, 10], page2.Items.Select(s => s.SequenceNumber));
        Assert.Equal(4, page2.TotalPages);
        var desc = await GetAsync<PagedResult<SessionSnapshot>>($"/api/runs/{run.Id}/sessions?sortBy=sequence&desc=true&pageSize=1");
        Assert.Equal(20, desc.Items.Single().SequenceNumber);

        // Bad filter → 400
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/runs/{run.Id}/sessions?status=Dancing")).StatusCode);

        // Session detail + event timeline
        var first = page2.Items[0];
        var events = await GetAsync<List<SessionEventDto>>($"/api/runs/{run.Id}/sessions/{first.Id}/events");
        Assert.True(events.Count >= 3);
        Assert.Equal(SessionStatus.Starting, events[0].ToStatus);
        Assert.True(events[^1].ToStatus is SessionStatus.Completed or SessionStatus.Failed);

        // Report – JSON
        var report = await GetAsync<RunReport>($"/api/runs/{run.Id}/report");
        Assert.Equal(20, report.Summary.Started);
        Assert.Equal(run.Completed, report.Summary.Completed);
        Assert.Equal(run.Failed, report.Summary.Failed);
        Assert.InRange(report.Summary.ObservedPeakConcurrency, 1, 20);
        Assert.NotEmpty(report.ConcurrencyTimeline);
        Assert.Contains("NOT views", report.DataSource, StringComparison.Ordinal);
        if (run.Failed > 0)
        {
            Assert.Equal(run.Failed, report.Errors.Single(e => e.ErrorCode == "STREAM_BROKEN").Count);
        }

        // Report – CSV
        var csvResponse = await _client.GetAsync($"/api/runs/{run.Id}/report?format=csv");
        Assert.Equal(HttpStatusCode.OK, csvResponse.StatusCode);
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv", csvResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"'), StringComparison.Ordinal);
        var lines = (await csvResponse.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.None);
        Assert.Contains("sessions_started,20", lines);
        var headerIndex = Array.FindIndex(lines, l => l.StartsWith("sequence,session_id", StringComparison.Ordinal));
        Assert.True(headerIndex > 0);
        Assert.Equal(20, lines.Skip(headerIndex + 1).Count(l => l.Length > 0));

        // Late cancels → 409 Conflict
        var lateCancel = await _client.PostAsync($"/api/runs/{run.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, lateCancel.StatusCode);
        var lateSessionCancel = await _client.PostAsync($"/api/runs/{run.Id}/sessions/{first.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, lateSessionCancel.StatusCode);

        // Listing
        var runs = await GetAsync<PagedResult<RunDto>>("/api/runs?status=Completed&pageSize=100");
        Assert.Contains(runs.Items, r => r.Id == run.Id);
    }

    [Fact]
    public async Task CancelEndpoint_StopsLiveRun()
    {
        var create = await _client.PostAsJsonAsync("/api/runs", new { name = "API cancel", sessionCount = 30, maxConcurrency = 5, playbackSpeed = 1, mediaItemIds = new[] { ShortClip } });
        var run = (await create.Content.ReadFromJsonAsync<RunDto>(Json))!;

        var live = await GetAsync<PagedResult<SessionSnapshot>>($"/api/runs/{run.Id}/sessions?status=active");
        Assert.True(live.TotalCount <= 5);

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync($"/api/runs/{run.Id}/cancel", null)).StatusCode);
        var final = await WaitForStatusAsync(run.Id, RunStatus.Cancelled);
        Assert.Equal(30, final.Cancelled + final.Completed + final.Failed);
        Assert.Equal(0, final.Active);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesEndpointsWithXmlComments()
    {
        var doc = await _client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/runs/{runId}/report", doc, StringComparison.Ordinal);
        Assert.Contains("/api/runs/{runId}/sessions/{sessionId}/cancel", doc, StringComparison.Ordinal);
        Assert.Contains("Results report", doc, StringComparison.Ordinal); // XML <summary> flowed into the document
        Assert.Contains("Streaming Automation API", doc, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SystemStats_ReportsEngineTelemetry()
    {
        var stats = await _client.GetFromJsonAsync<JsonElement>("/api/system/stats");
        Assert.True(stats.TryGetProperty("activeSessions", out _));
        Assert.True(stats.GetProperty("workingSetMb").GetDouble() > 0);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"{url} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<RunDto> WaitForStatusAsync(Guid runId, RunStatus status, int timeoutSeconds = 30)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        while (true)
        {
            var run = await GetAsync<RunDto>($"/api/runs/{runId}");
            if (run.Status == status)
            {
                return run;
            }

            await Task.Delay(100, cts.Token);
        }
    }
}
