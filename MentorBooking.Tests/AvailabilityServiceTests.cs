using MentorBooking.Models;
using MentorBooking.Services;

namespace MentorBooking.Tests;

public class AvailabilityServiceTests
{
    [Fact]
    public async Task GeneratesOneHourSlotsFromWindow()
    {
        using var db = TestHelpers.NewContext();
        var day = TestHelpers.NextWeekday(DayOfWeek.Monday);

        var mentor = new Mentor { DisplayName = "Test" };
        mentor.AvailabilityWindows.Add(new AvailabilityWindow
        {
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0)
        });
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        var svc = TestHelpers.NewAvailability(db);
        var slots = await svc.GetSlotsAsync(mentor.Id, day, day);

        // 09-10, 10-11, 11-12 => 3 slots
        Assert.Equal(3, slots.Count);
        Assert.All(slots, s => Assert.Equal(1, (s.EndUtc - s.StartUtc).TotalHours));
        Assert.All(slots, s => Assert.False(s.IsTaken));
    }

    [Fact]
    public async Task MarksSlotTakenWhenActiveBookingExists()
    {
        using var db = TestHelpers.NewContext();
        var day = TestHelpers.NextWeekday(DayOfWeek.Tuesday);

        var topic = new Topic { Name = "Math" };
        var mentor = new Mentor { DisplayName = "Test" };
        mentor.AvailabilityWindows.Add(new AvailabilityWindow
        {
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(12, 0)
        });
        db.Topics.Add(topic);
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        var takenStart = day.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        db.Bookings.Add(new Booking
        {
            MentorId = mentor.Id,
            TopicId = topic.Id,
            StartUtc = takenStart,
            EndUtc = takenStart.AddHours(1),
            StudentName = "S",
            StudentEmail = "s@x.com",
            Message = "help",
            Status = BookingStatus.Pending
        });
        await db.SaveChangesAsync();

        var svc = TestHelpers.NewAvailability(db);
        var slots = await svc.GetSlotsAsync(mentor.Id, day, day);

        Assert.Equal(2, slots.Count);
        Assert.True(slots.Single(s => s.StartUtc == takenStart).IsTaken);
        Assert.False(slots.Single(s => s.StartUtc == takenStart.AddHours(1)).IsTaken);
    }

    [Fact]
    public async Task DoesNotReturnPastSlots()
    {
        using var db = TestHelpers.NewContext();

        var mentor = new Mentor { DisplayName = "Test" };
        // Window on every weekday-of-today, but for a date in the past.
        mentor.AvailabilityWindows.Add(new AvailabilityWindow
        {
            DayOfWeek = DateTime.UtcNow.DayOfWeek,
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 0)
        });
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        var svc = TestHelpers.NewAvailability(db);
        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        var slots = await svc.GetSlotsAsync(mentor.Id, pastDate, pastDate);

        Assert.Empty(slots);
    }
}
