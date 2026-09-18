using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

public record BookingResult(bool Success, string? Error, Booking? Booking, string? EmailBody = null);

public class BookingService
{
    private readonly ApplicationDbContext _db;
    private readonly IMeetingService _meetingService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        ApplicationDbContext db,
        IMeetingService meetingService,
        IEmailSender emailSender,
        ILogger<BookingService> logger)
    {
        _db = db;
        _meetingService = meetingService;
        _emailSender = emailSender;
        _logger = logger;
    }

    // Mentors offering a topic, ordered so the least-loaded mentor comes first
    // (supports auto-assign while still letting the student pick).
    public async Task<List<Mentor>> GetMentorsForTopicAsync(int topicId)
    {
        var mentors = await _db.Mentors
            .Include(m => m.MentorTopics)
            .Where(m => m.MentorTopics.Any(mt => mt.TopicId == topicId))
            .ToListAsync();

        var loads = await _db.Bookings
            .Where(b => b.TopicId == topicId
                        && AvailabilityService.ActiveStatuses.Contains(b.Status))
            .GroupBy(b => b.MentorId)
            .Select(g => new { MentorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MentorId, x => x.Count);

        return mentors
            .OrderBy(m => loads.TryGetValue(m.Id, out var c) ? c : 0)
            .ThenBy(m => m.DisplayName)
            .ToList();
    }

    public async Task<Mentor?> AutoAssignMentorAsync(int topicId)
    {
        var mentors = await GetMentorsForTopicAsync(topicId);
        return mentors.FirstOrDefault();
    }

    // Creates a Pending booking that holds the slot until the mentor acts.
    public async Task<BookingResult> CreateBookingAsync(
        int mentorId, int topicId, DateTime startUtc,
        string studentName, string studentEmail, string message)
    {
        var endUtc = startUtc.AddHours(1);

        var mentor = await _db.Mentors
            .Include(m => m.MentorTopics)
            .FirstOrDefaultAsync(m => m.Id == mentorId);

        if (mentor is null)
            return new BookingResult(false, "Mentor not found.", null);

        if (!mentor.MentorTopics.Any(mt => mt.TopicId == topicId))
            return new BookingResult(false, "This mentor does not offer the selected topic.", null);

        var topic = await _db.Topics.FindAsync(topicId);
        if (topic is null)
            return new BookingResult(false, "Topic not found.", null);

        // Guard against double-booking (Pending or Confirmed holds the slot).
        var clash = await _db.Bookings.AnyAsync(b =>
            b.MentorId == mentorId
            && b.StartUtc == startUtc
            && AvailabilityService.ActiveStatuses.Contains(b.Status));

        if (clash)
            return new BookingResult(false, "That time slot is no longer available.", null);

        var booking = new Booking
        {
            MentorId = mentorId,
            TopicId = topicId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            StudentName = studentName,
            StudentEmail = studentEmail,
            Message = message,
            Status = BookingStatus.Pending,
            CreatedUtc = DateTime.UtcNow
        };

        _db.Bookings.Add(booking);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return new BookingResult(false, "That time slot is no longer available.", null);
        }

        // Acknowledge the request: email the student a copy and also return the body
        // so the confirmation screen can display it for copying.
        var emailBody = BuildRequestReceivedBody(booking, mentor.DisplayName, topic.Name);
        await _emailSender.SendAsync(booking.StudentEmail, booking.StudentName,
            "We received your mentoring request", TextToHtml(emailBody));

        // Notify the mentor so they can review and confirm/decline the pending request.
        await NotifyMentorOfRequestAsync(booking, mentor, topic.Name);

        return new BookingResult(true, null, booking, emailBody);
    }

    public async Task<BookingResult> ConfirmAsync(int bookingId)
    {
        var booking = await _db.Bookings
            .Include(b => b.Mentor)
            .Include(b => b.Topic)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking is null)
            return new BookingResult(false, "Booking not found.", null);

        if (booking.Status != BookingStatus.Pending)
            return new BookingResult(false, "Only pending bookings can be confirmed.", booking);

        booking.Status = BookingStatus.Confirmed;
        booking.MeetingUrl = _meetingService.CreateMeetingUrl(booking.Id);
        await _db.SaveChangesAsync();

        var emailBody = await SendConfirmationEmailAsync(booking);
        return new BookingResult(true, null, booking, emailBody);
    }

    public async Task<BookingResult> RejectAsync(int bookingId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new BookingResult(false, "A rejection reason is required.", null);

        var booking = await _db.Bookings
            .Include(b => b.Mentor)
            .Include(b => b.Topic)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking is null)
            return new BookingResult(false, "Booking not found.", null);

        if (booking.Status != BookingStatus.Pending)
            return new BookingResult(false, "Only pending bookings can be rejected.", booking);

        booking.Status = BookingStatus.Rejected;
        booking.RejectionReason = reason.Trim();
        await _db.SaveChangesAsync();

        var emailBody = await SendRejectionEmailAsync(booking);
        return new BookingResult(true, null, booking, emailBody);
    }

    // Mentor/Admin: cancel an active (Pending or Confirmed) booking with a required reason.
    // Frees the slot and emails the student.
    public async Task<BookingResult> CancelAsync(int bookingId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new BookingResult(false, "A cancellation reason is required.", null);

        var booking = await _db.Bookings
            .Include(b => b.Mentor)
            .Include(b => b.Topic)
            .FirstOrDefaultAsync(b => b.Id == bookingId);

        if (booking is null)
            return new BookingResult(false, "Booking not found.", null);

        if (!AvailabilityService.ActiveStatuses.Contains(booking.Status))
            return new BookingResult(false, "Only pending or confirmed bookings can be cancelled.", booking);

        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = reason.Trim();
        await _db.SaveChangesAsync();

        var emailBody = await SendCancellationEmailAsync(booking);
        return new BookingResult(true, null, booking, emailBody);
    }

    public async Task<BookingResult> DeleteAsync(int bookingId)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null)
            return new BookingResult(false, "Booking not found.", null);

        _db.Bookings.Remove(booking);
        await _db.SaveChangesAsync();
        return new BookingResult(true, null, booking);
    }

    // Admin: move a booking to a new start time if the slot is free.
    public async Task<BookingResult> RescheduleAsync(int bookingId, DateTime newStartUtc)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId);
        if (booking is null)
            return new BookingResult(false, "Booking not found.", null);

        var clash = await _db.Bookings.AnyAsync(b =>
            b.Id != bookingId
            && b.MentorId == booking.MentorId
            && b.StartUtc == newStartUtc
            && AvailabilityService.ActiveStatuses.Contains(b.Status));

        if (clash)
            return new BookingResult(false, "The target time slot is already taken.", booking);

        booking.StartUtc = newStartUtc;
        booking.EndUtc = newStartUtc.AddHours(1);
        await _db.SaveChangesAsync();
        return new BookingResult(true, null, booking);
    }

    private async Task<string> SendConfirmationEmailAsync(Booking booking)
    {
        var body =
$@"Hi {booking.StudentName},

Your mentoring session on {booking.Topic.Name} with {booking.Mentor.DisplayName} has been CONFIRMED.

When: {booking.StartUtc:yyyy-MM-dd HH:mm} - {booking.EndUtc:HH:mm}
Join the video call: {booking.MeetingUrl}

See you there!";

        await _emailSender.SendAsync(booking.StudentEmail, booking.StudentName,
            "Your mentoring session is confirmed", TextToHtml(body));
        return body;
    }

    private async Task<string> SendRejectionEmailAsync(Booking booking)
    {
        var body =
$@"Hi {booking.StudentName},

Unfortunately your mentoring request on {booking.Topic.Name} with {booking.Mentor.DisplayName}
for {booking.StartUtc:yyyy-MM-dd HH:mm} was DECLINED.

Reason:
{booking.RejectionReason}

You are welcome to book another slot.";

        await _emailSender.SendAsync(booking.StudentEmail, booking.StudentName,
            "Update on your mentoring request", TextToHtml(body));
        return body;
    }

    private async Task<string> SendCancellationEmailAsync(Booking booking)
    {
        var body =
$@"Hi {booking.StudentName},

Your mentoring session on {booking.Topic.Name} with {booking.Mentor.DisplayName}
scheduled for {booking.StartUtc:yyyy-MM-dd HH:mm} has been CANCELLED.

Reason:
{booking.CancellationReason}

You are welcome to book another slot.";

        await _emailSender.SendAsync(booking.StudentEmail, booking.StudentName,
            "Your mentoring session was cancelled", TextToHtml(body));
        return body;
    }

    // Emails the mentor that a new pending request needs their review. Best-effort:
    // mentors without a linked account/email (e.g. some seeded demo mentors) are skipped.
    private async Task NotifyMentorOfRequestAsync(Booking booking, Mentor mentor, string topicName)
    {
        if (string.IsNullOrEmpty(mentor.UserId))
        {
            _logger.LogInformation(
                "Mentor {MentorId} has no linked account; skipping mentor notification for booking {BookingId}.",
                mentor.Id, booking.Id);
            return;
        }

        var mentorEmail = await _db.Users
            .Where(u => u.Id == mentor.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(mentorEmail))
        {
            _logger.LogInformation(
                "Mentor {MentorId} has no email address; skipping mentor notification for booking {BookingId}.",
                mentor.Id, booking.Id);
            return;
        }

        var body = BuildMentorRequestBody(booking, mentor.DisplayName, topicName);
        await _emailSender.SendAsync(mentorEmail, mentor.DisplayName,
            "New mentoring request awaiting your review", TextToHtml(body));
    }

    // Plain-text notification emailed to the mentor when a new request is created.
    private static string BuildMentorRequestBody(Booking booking, string mentorName, string topicName)
    {
        return
$@"Hi {mentorName},

You have a new mentoring request awaiting your review:

Topic: {topicName}
Student: {booking.StudentName} ({booking.StudentEmail})
When: {booking.StartUtc:yyyy-MM-dd HH:mm} - {booking.EndUtc:HH:mm}

Student's message:
{booking.Message}

Please sign in to your mentor dashboard to confirm or decline this request.";
    }

    // Plain-text acknowledgement shown on screen and (as HTML) emailed to the student.
    private static string BuildRequestReceivedBody(Booking booking, string mentorName, string topicName)
    {
        return
$@"Hi {booking.StudentName},

Thanks for your request. We've received the following and it is now pending:

Topic: {topicName}
Mentor: {mentorName}
When: {booking.StartUtc:yyyy-MM-dd HH:mm} - {booking.EndUtc:HH:mm}

Your message:
{booking.Message}

{mentorName} will confirm or decline your request shortly. You'll receive an email at
{booking.StudentEmail} with the outcome, including a video link if the session is confirmed.";
    }

    private static string TextToHtml(string text) =>
        System.Net.WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br />");
}
