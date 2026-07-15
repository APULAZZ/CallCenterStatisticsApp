namespace CallCenterStatisticsApp.UI;

public sealed class BusyService
{
    public event EventHandler<BusyChangedEventArgs>? Changed;
    public IDisposable Begin(string message) { Changed?.Invoke(this, new(true, message)); return new Scope(this); }
    private void End() => Changed?.Invoke(this, new(false, string.Empty));
    private sealed class Scope(BusyService service) : IDisposable { private bool _disposed; public void Dispose() { if (_disposed) return; _disposed = true; service.End(); } }
}
public sealed record BusyChangedEventArgs(bool IsBusy, string Message);
