namespace BlazeCannon.Core.Models;

public class ScanResult
{
    public string VulnerabilityType { get; set; } = string.Empty;
    public ScanSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string FieldIdentifier { get; set; } = string.Empty;
    public BlazorMessage? TriggerMessage { get; set; }
    public BlazorMessage? ResponseMessage { get; set; }
    public DateTime FoundAt { get; set; } = DateTime.UtcNow;
}

public enum ScanSeverity { Critical, High, Medium, Low, Info }
