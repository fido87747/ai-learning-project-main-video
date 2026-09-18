using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

// Generates bookable 1-hour slots from a mentor's weekly availability windows,
// marking slots that already have an active (Pending/Confirmed) booking as taken.
//
// Prototype timezone note: availability window times and generated slots are treated
// as UTC. Bookings are stored in UTC. Displayed times are UTC.
public class AvailabilityService
{
    private readonly ApplicationDbContext _db;

    public AvailabilityService(ApplicationDbContext db)
    {
        _db = db;
    }

    public static readonly BookingStatus[] ActiveStatuses =
        { BookingStatus.Pending, BookingStatus.Confirmed };

    public async Task<List<TimeSlot>> GetSlotsAsync(int mentorId, DateOnly fromDate, DateOnly toDateInclusive)
    {
        var windows = await _db.AvailabilityWindows
            .Where(w => w.MentorId == mentorId)
            .ToListAsync();

        var rangeStartUtc = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rangeEndUtc = toDateInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var takenStarts = await _db.Bookings
            .Where(b => b.MentorId == mentorId
                        && ActiveStatuses.Contains(b.Status)
                        && b.StartUtc >= rangeStartUtc
                        && b.StartUtc < rangeEndUtc)
            .Select(b => b.StartUtc)
            .ToListAsync();

        var takenSet = takenStarts.ToHashSet();

        var slots = new List<TimeSlot>();
        var nowUtc = DateTime.UtcNow;

        for (var date = fromDate; date <= toDateInclusive; date = date.AddDays(1))
        {
            var dayWindows = windows.Where(w => w.DayOfWeek == date.DayOfWeek);
            foreach (var w in dayWindows)
            {
                var slotStart = w.StartTime;
                while (slotStart < w.EndTime && slotStart.AddHours(1) <= w.EndTime)
                {
                    var startUtc = date.ToDateTime(slotStart, DateTimeKind.Utc);
                    var endUtc = startUtc.AddHours(1);

                    // Only offer future slots.
                    if (startUtc > nowUtc)
                    {
                        slots.Add(new TimeSlot(mentorId, startUtc, endUtc, takenSet.Contains(startUtc)));
                    }

                    slotStart = slotStart.AddHours(1);
                }
            }
        }

        return slots.OrderBy(s => s.StartUtc).ToList();
    }
}
