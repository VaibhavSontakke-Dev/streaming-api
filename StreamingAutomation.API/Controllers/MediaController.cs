using Microsoft.AspNetCore.Mvc;
using StreamingAutomation.Application.Runs;

namespace StreamingAutomation.API.Controllers;

/// <summary>Synthetic test media catalogue (served by the local Test Media Server).</summary>
[ApiController]
[Route("api/media")]
[Produces("application/json")]
public sealed class MediaController(RunQueries queries) : ControllerBase
{
    /// <summary>Lists playable test media items.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MediaItemDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<MediaItemDto>> List(CancellationToken cancellationToken) => queries.ListMediaAsync(cancellationToken);
}
