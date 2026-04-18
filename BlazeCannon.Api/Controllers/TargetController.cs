using BlazeCannon.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlazeCannon.Api.Controllers;

[ApiController]
[Route("api/target")]
public class TargetController : ControllerBase
{
    private readonly TargetConfig _target;

    public TargetController(TargetConfig target)
    {
        _target = target;
    }

    [HttpGet]
    public IActionResult Get() => Ok(_target);

    [HttpPut]
    public IActionResult Put([FromBody] TargetConfig update)
    {
        if (update is null) return BadRequest(new { error = "TargetConfig body required." });

        _target.BaseUrl = update.BaseUrl;
        _target.BlazorHubPath = update.BlazorHubPath;
        _target.ConnectionTimeout = update.ConnectionTimeout;
        _target.UseMessagePack = update.UseMessagePack;
        _target.CustomHeaders = update.CustomHeaders ?? new Dictionary<string, string>();
        _target.AuthCookie = update.AuthCookie;

        return Ok(_target);
    }
}
