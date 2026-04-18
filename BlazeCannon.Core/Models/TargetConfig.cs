namespace BlazeCannon.Core.Models;

public class TargetConfig
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string BlazorHubPath { get; set; } = "/_blazor";
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseMessagePack { get; set; } = false;
    public Dictionary<string, string> CustomHeaders { get; set; } = new();
    public string? AuthCookie { get; set; }
}
