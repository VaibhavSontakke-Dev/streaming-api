using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StreamingAutomation.Application.Common.Interfaces;
using StreamingAutomation.Application.Playback;
using StreamingAutomation.Application.Common.Exceptions;
using StreamingAutomation.Domain.Entities;
using StreamingAutomation.Domain.Enums;
using StreamingAutomation.Domain.Exceptions;

namespace StreamingAutomation.Application.Runs;

/// <summary>Use cases that change test runs (queries live in later steps).</summary>
public sealed class TestRunService(
    IApplicationDbContext db,
    IPlaybackOrchestrator orchestrator,
    IValidator<CreateRunRequest> validator,
    TimeProvider clock)
{
    public async Task<Guid> CreateAndStartAsync(CreateRunRequest request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var mediaQuery = db.MediaItems.AsNoTracking();
        if (request.MediaItemIds is { Count: > 0 } ids)
        {
            mediaQuery = mediaQuery.Where(m => ids.Contains(m.Id));
        }

        var media = await mediaQuery.OrderBy(m => m.Title).ToListAsync(cancellationToken);
        if (media.Count == 0 || (request.MediaItemIds is { Count: > 0 } && media.Count != request.MediaItemIds.Distinct().Count()))
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure(nameof(request.MediaItemIds), "One or more media items do not exist.")]);
        }

        var now = clock.GetUtcNow();
        var run = new TestRun(
            Guid.CreateVersion7(),
            request.Name.Trim(),
            media[0].Platform,
            request.SessionCount,
            request.MaxConcurrency ?? request.SessionCount,
            request.RampUpSeconds,
            request.PlaybackSpeed,
            request.SimulatedFailureRate,
            now);

        db.TestRuns.Add(run);
        for (var i = 0; i < request.SessionCount; i++)
        {
            var item = media[i % media.Count]; // round-robin media assignment
            db.PlaybackSessions.Add(new PlaybackSession(Guid.CreateVersion7(), run.Id, item.Id, i + 1, item.DurationSeconds, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await orchestrator.StartRunAsync(run.Id, cancellationToken);
        return run.Id;
    }

    /// <summary>Requests cancellation of an executing run. 404 if unknown, 409 if it is not running.</summary>
    public async Task CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (orchestrator.CancelRun(runId))
        {
            return;
        }

        var status = await db.TestRuns.Where(r => r.Id == runId).Select(r => (RunStatus?)r.Status).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Test run", runId);
        throw new DomainException($"Run cannot be cancelled because it is {status}.");
    }

    /// <summary>Requests cancellation of one session. 404 if unknown, 409 if it has already finished.</summary>
    public async Task CancelSessionAsync(Guid runId, Guid sessionId, CancellationToken cancellationToken)
    {
        if (orchestrator.CancelSession(runId, sessionId))
        {
            return;
        }

        var status = await db.PlaybackSessions.Where(s => s.Id == sessionId && s.TestRunId == runId)
                .Select(s => (SessionStatus?)s.Status).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Session", sessionId);
        throw new DomainException($"Session cannot be cancelled because it is {status}.");
    }
}
