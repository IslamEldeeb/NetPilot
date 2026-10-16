namespace NetPilot.Web;

// The shell can inject this flag to show an ordinary POST /logout form.
public sealed class DashboardLoginOptions(string? password)
{
    public bool Enabled { get; } = !string.IsNullOrEmpty(password);
}
