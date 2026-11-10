namespace NetPilot.Web;

// The shell can inject this flag to show an ordinary POST /logout form.
public sealed class DashboardLoginOptions(string? username, string? password)
{
    public string Username { get; } = !string.IsNullOrEmpty(username) ? username : "admin";
    public string Password { get; } = !string.IsNullOrEmpty(password) ? password : "admin";
    public bool Enabled { get; } = !string.IsNullOrEmpty(password);
}
