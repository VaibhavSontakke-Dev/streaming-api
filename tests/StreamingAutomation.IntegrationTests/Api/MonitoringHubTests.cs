using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using StreamingAutomation.Application.Monitoring;
using StreamingAutomation.Application.Runs;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.IntegrationTests.Engine;

namespace StreamingAutomation.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class MonitoringHubTests(ApiFactory api)
{
    [Fact]
    public async Task Subscriber_ReceivesInitialState_ThrottledDeltas_AndFinishNotice()
    {
        var client = api.CreateClient();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(client.BaseAddress!, "hubs/monitoring"), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer has no real sockets
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var runUpdates = new ConcurrentQueue<RunUpdate>();
        var overviews = new ConcurrentQueue<OverviewUpdate>();
        var finished = new TaskCompletionSource<RunFinishedNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid runId = default;

        connection.On<RunUpdate>("RunUpdated", u => runUpdates.Enqueue(u));
        connection.On<OverviewUpdate>("OverviewUpdated", o => overviews.Enqueue(o));
        connection.On<RunFinishedNotice>("RunFinished", n =>
        {
            if (n.RunId == runId)
            {
                finished.TrySetResult(n);
            }
        });
        await connection.StartAsync();

        // 30 sessions of a 60 s clip at 15x → about 4 s of playback
        var response = await client.PostAsJsonAsync("/api/runs", new
        {
            name = "SignalR",
            sessionCount = 30,
            playbackSpeed = 15,
            mediaItemIds = new[] { "8f1c2a10-0000-4000-8000-000000000001" },
        });
        var run = (await response.Content.ReadFromJsonAsync<RunDto>(RunsApiTests.Json))!;
        runId = run.Id;
        var sw = Stopwatch.StartNew();

        var initial = await connection.InvokeAsync<RunLiveState?>("SubscribeToRun", runId);
        Assert.NotNull(initial);
        Assert.Equal(30, initial!.Sessions.Count);

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        sw.Stop();

        var mine = runUpdates.Where(u => u.RunId == runId).ToList();
        Assert.NotEmpty(mine);
        Assert.Contains(mine, u => u.ChangedSessions.Count > 0);
        Assert.Contains(overviews, o => o.ActiveRuns.Any(r => r.RunId == runId && r.Stats.Active > 0));

        // Throttling: ~1 message per 500 ms tick, even though sessions produce hundreds of events per second.
        var maxExpected = (int)Math.Ceiling(sw.Elapsed.TotalMilliseconds / 500) + 2;
        Assert.True(mine.Count <= maxExpected, $"{mine.Count} updates in {sw.Elapsed.TotalSeconds:F1}s (max {maxExpected})");

        // Applying the deltas on top of the initial state reproduces the final state.
        var state = initial.Sessions.ToDictionary(s => s.Id);
        foreach (var update in mine)
        {
            foreach (var s in update.ChangedSessions)
            {
                state[s.Id] = s;
            }
        }

        Assert.True(state.Values.Count(s => s.Status is SessionStatus.Completed) >= 25, "deltas should carry sessions to completion");

        // The 'finished' notice tells clients to re-fetch the final state over REST.
        var final = await client.GetFromJsonAsync<RunDto>($"/api/runs/{runId}", RunsApiTests.Json);
        Assert.Equal(RunStatus.Completed, final!.Status);
        Assert.Equal(30, final.Completed);
    }

    [Fact]
    public async Task SubscribeToUnknownRun_ReturnsNull()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "hubs/monitoring"), o =>
            {
                o.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        await connection.StartAsync();

        Assert.Null(await connection.InvokeAsync<RunLiveState?>("SubscribeToRun", Guid.NewGuid()));
        _ = api.Services.GetRequiredService<LiveMonitor>(); // registered as singleton
    }
}
