using BlazeCannon.Core.Models;

namespace BlazeCannon.Api.Services;

/// <summary>
/// Holds a queue of BlazorMessages the operator wants to replay. The Angular UI
/// stages messages here before firing them at the target. Thread-safe.
/// </summary>
public class ReplayStagingService
{
    private readonly object _lock = new();
    private readonly List<BlazorMessage> _staged = new();

    public event Action? OnStageChanged;

    public IReadOnlyList<BlazorMessage> Staged
    {
        get { lock (_lock) return _staged.ToList().AsReadOnly(); }
    }

    public void Stage(BlazorMessage message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        lock (_lock) _staged.Add(message);
        OnStageChanged?.Invoke();
    }

    public bool Remove(int index)
    {
        bool removed;
        lock (_lock)
        {
            if (index < 0 || index >= _staged.Count) return false;
            _staged.RemoveAt(index);
            removed = true;
        }
        if (removed) OnStageChanged?.Invoke();
        return removed;
    }

    public void Clear()
    {
        bool had;
        lock (_lock)
        {
            had = _staged.Count > 0;
            _staged.Clear();
        }
        if (had) OnStageChanged?.Invoke();
    }
}
