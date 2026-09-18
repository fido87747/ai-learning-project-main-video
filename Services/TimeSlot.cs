namespace MentorBooking.Services;

// A bookable 1-hour slot for a mentor, in UTC.
public record TimeSlot(int MentorId, DateTime StartUtc, DateTime EndUtc, bool IsTaken);
