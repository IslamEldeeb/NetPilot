using System.Globalization;

namespace NetPilot.Core.Notifications;

public class NotificationOptions
{
    public QuietHoursOptions QuietHours { get; set; } = new();
    // Replace the fallback with a real contact email or HTTPS page for the push service.
    public string VapidSubject { get; set; } = "mailto:netpilot@localhost";
    public double DedupeWindowHours { get; set; } = 24;

    public void Validate()
    {
        if (!double.IsFinite(DedupeWindowHours) || DedupeWindowHours < 0 || DedupeWindowHours > 87600)
            throw new ArgumentException("NetPilot:Notifications:DedupeWindowHours must be between 0 and 87600.");
        if (!Uri.TryCreate(VapidSubject, UriKind.Absolute, out var subject) || subject.Scheme is not ("mailto" or "https"))
            throw new ArgumentException("NetPilot:Notifications:VapidSubject must be a mailto: or https: URL.");
        QuietHours.Validate();
    }
}

public class QuietHoursOptions
{
    public bool Enabled { get; set; }
    public string Start { get; set; } = "23:00";
    public string End { get; set; } = "07:00";

    internal TimeOnly StartTime => Parse(Start);
    internal TimeOnly EndTime => Parse(End);
    internal void Validate() { _ = StartTime; _ = EndTime; }

    private static TimeOnly Parse(string text) => TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var time) ? time : throw new ArgumentException("Quiet hours must use HH:mm.");
}
