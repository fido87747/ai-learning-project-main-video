using MentorBooking.Data;
using MentorBooking.Models;

namespace MentorBooking.Tests;

public class BookingServiceTests
{
    private static async Task<(Mentor mentor, Topic topic)> SeedMentorWithTopic(ApplicationDbContext db)
    {
        var topic = new Topic { Name = "Math" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var mentor = new Mentor { DisplayName = "Alice" };
        mentor.MentorTopics.Add(new MentorTopic { TopicId = topic.Id });
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        return (mentor, topic);
    }

    private static DateTime FutureSlot() =>
        DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(3).AddHours(10), DateTimeKind.Utc);

    [Fact]
    public async Task CreateBooking_Succeeds_AndIsPending()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var email = new FakeEmailSender();
        var svc = TestHelpers.NewBookingService(db, email);

        var result = await svc.CreateBookingAsync(mentor.Id, topic.Id, FutureSlot(),
            "Bob", "bob@x.com", "Need help with algebra");

        Assert.True(result.Success);
        Assert.NotNull(result.Booking);
        Assert.Equal(BookingStatus.Pending, result.Booking!.Status);

        // An acknowledgement email is sent to the student and its body is returned for display.
        Assert.False(string.IsNullOrWhiteSpace(result.EmailBody));
        Assert.Contains("Need help with algebra", result.EmailBody);
        Assert.Single(email.Sent);
        Assert.Equal("bob@x.com", email.Sent[0].To);
    }

    [Fact]
    public async Task CreateBooking_NotifiesMentor_WhenMentorHasAccount()
    {
        using var db = TestHelpers.NewContext();

        var topic = new Topic { Name = "Math" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var mentorUser = new ApplicationUser
        {
            UserName = "alice@mentorbooking.local",
            Email = "alice@mentorbooking.local",
            FullName = "Alice"
        };
        db.Users.Add(mentorUser);
        await db.SaveChangesAsync();

        var mentor = new Mentor { DisplayName = "Alice", UserId = mentorUser.Id };
        mentor.MentorTopics.Add(new MentorTopic { TopicId = topic.Id });
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        var svc = TestHelpers.NewBookingService(db, email);

        var result = await svc.CreateBookingAsync(mentor.Id, topic.Id, FutureSlot(),
            "Bob", "bob@x.com", "Need help with algebra");

        Assert.True(result.Success);

        // Both the student acknowledgement and the mentor review notification are sent.
        Assert.Equal(2, email.Sent.Count);
        Assert.Contains(email.Sent, m => m.To == "bob@x.com");

        var mentorMail = email.Sent.Single(m => m.To == "alice@mentorbooking.local");
        Assert.Contains("awaiting your review", mentorMail.Subject);
        Assert.Contains("Need help with algebra", mentorMail.Body);
        Assert.Contains("bob@x.com", mentorMail.Body);
    }

    [Fact]
    public async Task CreateBooking_Rejects_DoubleBooking()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var svc = TestHelpers.NewBookingService(db);
        var slot = FutureSlot();

        var first = await svc.CreateBookingAsync(mentor.Id, topic.Id, slot, "A", "a@x.com", "hello");
        var second = await svc.CreateBookingAsync(mentor.Id, topic.Id, slot, "B", "b@x.com", "hello");

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("no longer available", second.Error);
    }

    [Fact]
    public async Task CreateBooking_Rejects_TopicMentorMismatch()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, _) = await SeedMentorWithTopic(db);
        var otherTopic = new Topic { Name = "Cooking" };
        db.Topics.Add(otherTopic);
        await db.SaveChangesAsync();

        var svc = TestHelpers.NewBookingService(db);
        var result = await svc.CreateBookingAsync(mentor.Id, otherTopic.Id, FutureSlot(),
            "Bob", "bob@x.com", "hi");

        Assert.False(result.Success);
        Assert.Contains("does not offer", result.Error);
    }

    [Fact]
    public async Task Confirm_SetsConfirmed_GeneratesMeetingUrl_AndEmails()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var email = new FakeEmailSender();
        var svc = TestHelpers.NewBookingService(db, email);

        var created = await svc.CreateBookingAsync(mentor.Id, topic.Id, FutureSlot(),
            "Bob", "bob@x.com", "hi");
        email.Sent.Clear(); // ignore the request-received acknowledgement
        var result = await svc.ConfirmAsync(created.Booking!.Id);

        Assert.True(result.Success);
        Assert.Equal(BookingStatus.Confirmed, result.Booking!.Status);
        Assert.False(string.IsNullOrEmpty(result.Booking.MeetingUrl));
        Assert.Contains("meet.jit.si", result.Booking.MeetingUrl);
        Assert.Single(email.Sent);
        Assert.Equal("bob@x.com", email.Sent[0].To);
        Assert.False(string.IsNullOrWhiteSpace(result.EmailBody));
        Assert.Contains(result.Booking.MeetingUrl!, result.EmailBody);
    }

    [Fact]
    public async Task Reject_RequiresReason()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var svc = TestHelpers.NewBookingService(db);

        var created = await svc.CreateBookingAsync(mentor.Id, topic.Id, FutureSlot(),
            "Bob", "bob@x.com", "hi");

        var noReason = await svc.RejectAsync(created.Booking!.Id, "   ");
        Assert.False(noReason.Success);

        var withReason = await svc.RejectAsync(created.Booking!.Id, "Not my subject");
        Assert.True(withReason.Success);
        Assert.Equal(BookingStatus.Rejected, withReason.Booking!.Status);
        Assert.Equal("Not my subject", withReason.Booking.RejectionReason);
        Assert.Contains("Not my subject", withReason.EmailBody);
    }

    [Fact]
    public async Task Reject_FreesSlot_ForNewBooking()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var svc = TestHelpers.NewBookingService(db);
        var slot = FutureSlot();

        var first = await svc.CreateBookingAsync(mentor.Id, topic.Id, slot, "A", "a@x.com", "hi");
        await svc.RejectAsync(first.Booking!.Id, "busy");

        var second = await svc.CreateBookingAsync(mentor.Id, topic.Id, slot, "B", "b@x.com", "hi");
        Assert.True(second.Success);
    }

    [Fact]
    public async Task AutoAssign_PicksLeastLoadedMentor()
    {
        using var db = TestHelpers.NewContext();
        var topic = new Topic { Name = "Math" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();

        var busy = new Mentor { DisplayName = "Busy" };
        busy.MentorTopics.Add(new MentorTopic { TopicId = topic.Id });
        var free = new Mentor { DisplayName = "Free" };
        free.MentorTopics.Add(new MentorTopic { TopicId = topic.Id });
        db.Mentors.AddRange(busy, free);
        await db.SaveChangesAsync();

        var svc = TestHelpers.NewBookingService(db);
        // Give "busy" an active booking so it is more loaded.
        await svc.CreateBookingAsync(busy.Id, topic.Id, FutureSlot(), "X", "x@x.com", "hi");

        var assigned = await svc.AutoAssignMentorAsync(topic.Id);
        Assert.NotNull(assigned);
        Assert.Equal("Free", assigned!.DisplayName);
    }

    [Fact]
    public async Task Cancel_RequiresReason_AndEmailsStudent()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var email = new FakeEmailSender();
        var svc = TestHelpers.NewBookingService(db, email);

        var created = await svc.CreateBookingAsync(mentor.Id, topic.Id, FutureSlot(),
            "Bob", "bob@x.com", "hi");
        await svc.ConfirmAsync(created.Booking!.Id);
        email.Sent.Clear();

        var noReason = await svc.CancelAsync(created.Booking!.Id, "  ");
        Assert.False(noReason.Success);

        var cancelled = await svc.CancelAsync(created.Booking!.Id, "Mentor is unavailable");
        Assert.True(cancelled.Success);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Booking!.Status);
        Assert.Equal("Mentor is unavailable", cancelled.Booking.CancellationReason);
        Assert.Single(email.Sent);
        Assert.Equal("bob@x.com", email.Sent[0].To);
        Assert.Contains("Mentor is unavailable", cancelled.EmailBody);
    }

    [Fact]
    public async Task Cancel_FreesSlot_ForNewBooking()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var svc = TestHelpers.NewBookingService(db);
        var slot = FutureSlot();

        var first = await svc.CreateBookingAsync(mentor.Id, topic.Id, slot, "A", "a@x.com", "hi");
        await svc.ConfirmAsync(first.Booking!.Id);
        await svc.CancelAsync(first.Booking!.Id, "conflict");

        var second = await svc.CreateBookingAsync(mentor.Id, topic.Id, slot, "B", "b@x.com", "hi");
        Assert.True(second.Success);
    }

    [Fact]
    public async Task Cancel_Rejects_NonActiveBooking()
    {
        using var db = TestHelpers.NewContext();
        var (mentor, topic) = await SeedMentorWithTopic(db);
        var svc = TestHelpers.NewBookingService(db);

        var created = await svc.CreateBookingAsync(mentor.Id, topic.Id, FutureSlot(),
            "Bob", "bob@x.com", "hi");
        await svc.RejectAsync(created.Booking!.Id, "no");

        var result = await svc.CancelAsync(created.Booking!.Id, "changed my mind");
        Assert.False(result.Success);
        Assert.Contains("pending or confirmed", result.Error);
    }
}
