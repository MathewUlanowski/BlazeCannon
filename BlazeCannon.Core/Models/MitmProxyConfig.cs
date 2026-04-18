namespace BlazeCannon.Core.Models;

public class MitmProxyConfig
{
    public string TargetBaseUrl { get; set; } = "http://localhost:5000";
    public int ListenPort { get; set; } = 5001;
    public string BlazorHubPath { get; set; } = "/_blazor";
}
