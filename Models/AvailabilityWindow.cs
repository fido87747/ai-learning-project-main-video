namespace MentorBooking.Models;

// A recurring weekly availability window for a mentor (e.g., Monday 09:00-12:00).
// Bookable 1-hour slots are derived from these windows minus taken bookings.
public class AvailabilityWindow
{
    public int Id { get; set; }

    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    // Stored as time-of-day. Windows should align to whole hours for 1-hour slots.
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
