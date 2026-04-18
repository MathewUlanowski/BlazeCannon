namespace BlazeCannon.Core.Models;

public class ScanProgress
{
    public ScanStatus Status { get; set; }
    public int TotalPayloads { get; set; }
    public int CompletedPayloads { get; set; }
    public int VulnerabilitiesFound { get; set; }
    public string CurrentPage { get; set; } = string.Empty;
    public string CurrentPayload { get; set; } = string.Empty;
    public double PercentComplete => TotalPayloads > 0 ? (double)CompletedPayloads / TotalPayloads * 100 : 0;
}

public enum ScanStatus { Idle, Connecting, Scanning, Complete, Error, Cancelled }
