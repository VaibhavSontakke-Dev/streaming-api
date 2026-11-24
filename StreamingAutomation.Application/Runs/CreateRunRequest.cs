using FluentValidation;
using Microsoft.Extensions.Options;
using StreamingAutomation.Application.Common.Options;

namespace StreamingAutomation.Application.Runs;

/// <summary>Request to start a new test run.</summary>
/// <param name="Name">Human-readable label.</param>
/// <param name="SessionCount">Number of simulated playback sessions.</param>
/// <param name="MaxConcurrency">Max sessions playing at once (defaults to <paramref name="SessionCount"/>).</param>
/// <param name="RampUpSeconds">Spread session starts evenly over this many seconds.</param>
/// <param name="PlaybackSpeed">Time scale: 1 = real time, 10 = 10x faster.</param>
/// <param name="SimulatedFailureRate">Fraction (0–1) of sessions whose stream is broken by the test media server.</param>
/// <param name="MediaItemIds">Media to play (round-robin). Empty = whole catalogue.</param>
public sealed record CreateRunRequest(
    string Name,
    int SessionCount,
    int? MaxConcurrency = null,
    int RampUpSeconds = 0,
    double PlaybackSpeed = 1,
    double SimulatedFailureRate = 0,
    IReadOnlyList<Guid>? MediaItemIds = null);

public sealed class CreateRunRequestValidator : AbstractValidator<CreateRunRequest>
{
    public CreateRunRequestValidator(IOptions<PlaybackOptions> options)
    {
        var o = options.Value;

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SessionCount).InclusiveBetween(1, o.MaxSessionsPerRun);
        RuleFor(x => x.MaxConcurrency)
            .InclusiveBetween(1, o.MaxGlobalConcurrency)
            .When(x => x.MaxConcurrency.HasValue);
        RuleFor(x => x.RampUpSeconds).InclusiveBetween(0, 3600);
        RuleFor(x => x.PlaybackSpeed).GreaterThan(0).LessThanOrEqualTo(o.MaxPlaybackSpeed);
        RuleFor(x => x.SimulatedFailureRate).InclusiveBetween(0, 1);
        RuleFor(x => x.MediaItemIds).Must(ids => ids is null || ids.Count <= 50).WithMessage("At most 50 media items per run.");
    }
}
