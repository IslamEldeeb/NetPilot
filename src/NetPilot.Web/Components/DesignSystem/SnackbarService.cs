namespace NetPilot.Web.Components.DesignSystem;

public sealed record SnackbarMessage(string Text, Func<Task>? Undo, TimeSpan? Duration);

/// <summary>Scoped to one circuit; errors can opt into a persistent message with a null duration.</summary>
public sealed class SnackbarService
{
    public event Action? Changed;
    public SnackbarMessage? Current { get; private set; }

    public void Show(string text, Func<Task>? undo = null) =>
        Show(new SnackbarMessage(text, undo, TimeSpan.FromSeconds(10)));

    public void Show(SnackbarMessage message) { Current = message; Changed?.Invoke(); }
    public void Dismiss() { Current = null; Changed?.Invoke(); }
}
