using BlazeCannon.Core.Models;

namespace BlazeCannon.App.Services;

public class AppStateService
{
    public TargetConfig TargetConfig { get; set; } = new();
    public bool IsConnected { get; set; }
    public List<BlazorMessage> Messages { get; } = new();
    public List<ScanResult> ScanResults { get; } = new();

    public event Action? OnStateChanged;
    public void NotifyStateChanged() => OnStateChanged?.Invoke();
}
