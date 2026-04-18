using BlazeCannon.Api.Services;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlazeCannon.Api.Controllers;

[ApiController]
[Route("api/replay")]
public class ReplayController : ControllerBase
{
    private readonly IMitmProxy _proxy;
    private readonly ReplayStagingService _staging;
    private readonly ILogger<ReplayController> _logger;

    public ReplayController(IMitmProxy proxy, ReplayStagingService staging, ILogger<ReplayController> logger)
    {
        _proxy = proxy;
        _staging = staging;
        _logger = logger;
    }

    [HttpGet("staged")]
    public IActionResult GetStaged() => Ok(_staging.Staged);

    [HttpPost("stage")]
    public IActionResult Stage([FromBody] BlazorMessage message)
    {
        if (message is null) return BadRequest(new { error = "Message body required." });
        _staging.Stage(message);
        return Ok(_staging.Staged);
    }

    [HttpDelete("staged/{index:int}")]
    public IActionResult RemoveStaged(int index)
    {
        if (!_staging.Remove(index))
            return NotFound(new { error = $"No staged message at index {index}." });
        return NoContent();
    }

    [HttpDelete("staged")]
    public IActionResult ClearStaged()
    {
        _staging.Clear();
        return NoContent();
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send([FromBody] BlazorMessage message, CancellationToken ct)
    {
        if (message is null) return BadRequest(new { error = "Message body required." });

        try
        {
            await _proxy.ReplayMessageAsync(message, ct);
            return Ok(new { sentAt = DateTime.UtcNow, error = (string?)null });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replay failed");
            return Ok(new { sentAt = (DateTime?)null, error = ex.Message });
        }
    }
}
