using System.Text;
using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

// Admin-only CSV report endpoints, matching the on-screen reports.
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Admin));

        group.MapGet("/mentors.csv", async (ApplicationDbContext db) =>
        {
            var nowUtc = DateTime.UtcNow;

            var mentors = await db.Mentors
                .Include(m => m.MentorTopics).ThenInclude(mt => mt.Topic)
                .OrderBy(m => m.DisplayName)
                .ToListAsync();

            var users = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Email);

            var bookings = await db.Bookings
                .Select(b => new { b.MentorId, b.StartUtc, b.Status })
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Mentor,Email,Skills,Upcoming meetings,Total bookings");

            foreach (var m in mentors)
            {
                var email = m.UserId is not null && users.TryGetValue(m.UserId, out var e) ? e ?? "" : "";
                var skills = string.Join("; ", m.MentorTopics.Select(mt => mt.Topic.Name).OrderBy(n => n));
                var upcoming = bookings.Count(b => b.MentorId == m.Id
                    && b.StartUtc >= nowUtc
                    && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed));
                var total = bookings.Count(b => b.MentorId == m.Id);

                sb.AppendLine(string.Join(",",
                    Csv(m.DisplayName), Csv(email), Csv(skills), upcoming, total));
            }

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "mentors.csv");
        });

        group.MapGet("/upcoming.csv", async (ApplicationDbContext db) =>
        {
            var nowUtc = DateTime.UtcNow;

            var rows = await db.Bookings
                .Include(b => b.Mentor)
                .Include(b => b.Topic)
                .Where(b => b.StartUtc >= nowUtc
                    && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
                .OrderBy(b => b.StartUtc)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Mentor,Date,Regular user,Email,Topic,Status,Meeting URL");

            foreach (var b in rows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(b.Mentor.DisplayName),
                    Csv(b.StartUtc.ToString("yyyy-MM-dd HH:mm")),
                    Csv(b.StudentName),
                    Csv(b.StudentEmail),
                    Csv(b.Topic.Name),
                    Csv(b.Status.ToString()),
                    Csv(b.MeetingUrl ?? "")));
            }

            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "upcoming-meetings.csv");
        });
    }

    private static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
