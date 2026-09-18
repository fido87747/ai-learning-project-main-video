using MentorBooking.Data;
using MentorBooking.Models;
using MentorBooking.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MentorBooking.Tests;

// Shared helpers for building an in-memory DbContext and services.
public static class TestHelpers
{
    public static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    public static AvailabilityService NewAvailability(ApplicationDbContext db) => new(db);

    public static MentorProfileService NewProfileService(ApplicationDbContext db) => new(db);

    public static BookingService NewBookingService(ApplicationDbContext db, FakeEmailSender? email = null)
    {
        var meeting = new JitsiMeetingService(Options.Create(new MeetingSettings()));
        return new BookingService(db, meeting, email ?? new FakeEmailSender(),
            NullLogger<BookingService>.Instance);
    }

    // Returns the next occurrence of the given weekday strictly in the future (UTC).
    public static DateOnly NextWeekday(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        while (date.DayOfWeek != day)
        {
            date = date.AddDays(1);
        }
        return date;
    }
}

public class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        Sent.Add((toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
