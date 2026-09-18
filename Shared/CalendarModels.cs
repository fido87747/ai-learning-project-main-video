namespace MentorBooking.Shared;

public enum CalendarViewMode
{
    Daily,
    Weekly
}

// A single item shown on the calendar grid, positioned by StartUtc.
public class CalendarEntry
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty; // free, pending, confirmed, taken, rejected
    public object? Data { get; set; }
}
