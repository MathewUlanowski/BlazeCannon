using BlazeCannon.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BlazeCannon.Api.Controllers;

[ApiController]
[Route("api/proxy")]
public class ProxyController : ControllerBase
{
    private readonly IMitmProxy _proxy;

    public ProxyController(IMitmProxy proxy)
    {
        _proxy = proxy;
    }

    [HttpGet("status")]
    public IActionResult Status()
    {
        var uiPort = int.Parse(Environment.GetEnvironmentVariable("BLAZECANNON_UI_PORT") ?? "8080");
        var proxyPort = int.Parse(Environment.GetEnvironmentVariable("BLAZECANNON_PROXY_PORT") ?? "5001");

        return Ok(new
        {
            isRunning = _proxy.IsRunning,
            uiPort,
            proxyPort,
            activeSessionCount = _proxy.ActiveSessionCount,
            capturedCount = _proxy.CapturedMessages.Count
        });
    }
}
