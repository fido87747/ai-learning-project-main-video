using MentorBooking.Data;
using MentorBooking.Models;
using MentorBooking.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace MentorBooking.Tests;

public class MentorManagementServiceTests
{
    // Builds a UserManager/RoleManager backed by the same in-memory store as the context
    // is not trivial, so we mock them for the parts we assert on.
    private static MentorManagementService NewService(
        ApplicationDbContext db,
        UserManager<ApplicationUser>? userManager = null,
        RoleManager<IdentityRole>? roleManager = null)
    {
        return new MentorManagementService(
            db,
            userManager ?? MockUserManager().Object,
            roleManager ?? MockRoleManager().Object);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);
        mgr.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        mgr.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        return mgr;
    }

    private static Mock<RoleManager<IdentityRole>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var mgr = new Mock<RoleManager<IdentityRole>>(
            store.Object, null!, null!, null!, null!);
        mgr.Setup(m => m.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        return mgr;
    }

    [Fact]
    public async Task AddTopic_Succeeds_AndPreventsDuplicates()
    {
        using var db = TestHelpers.NewContext();
        var svc = NewService(db);

        var first = await svc.AddTopicAsync("Robotics");
        var dup = await svc.AddTopicAsync("robotics");

        Assert.True(first.Success);
        Assert.False(dup.Success);
        Assert.Equal(1, await db.Topics.CountAsync());
    }

    [Fact]
    public async Task DeleteTopic_Blocked_WhenBookingsExist()
    {
        using var db = TestHelpers.NewContext();
        var topic = new Topic { Name = "Math" };
        var mentor = new Mentor { DisplayName = "M" };
        db.Topics.Add(topic);
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        db.Bookings.Add(new Booking
        {
            MentorId = mentor.Id,
            TopicId = topic.Id,
            StartUtc = DateTime.UtcNow.AddDays(1),
            EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
            StudentName = "S",
            StudentEmail = "s@x.com",
            Message = "hi"
        });
        await db.SaveChangesAsync();

        var svc = NewService(db);
        var result = await svc.DeleteTopicAsync(topic.Id);

        Assert.False(result.Success);
        Assert.Contains("bookings", result.Error);
    }

    [Fact]
    public async Task CreateMentor_CreatesMentorWithSkills()
    {
        using var db = TestHelpers.NewContext();
        var t1 = new Topic { Name = "Math" };
        var t2 = new Topic { Name = "Physics" };
        db.Topics.AddRange(t1, t2);
        await db.SaveChangesAsync();

        var svc = NewService(db);
        var result = await svc.CreateMentorAsync(
            "new@x.com", "Passw0rd!", "New Mentor", "bio", new[] { t1.Id, t2.Id });

        Assert.True(result.Success);
        var mentor = await db.Mentors.Include(m => m.MentorTopics).SingleAsync();
        Assert.Equal("New Mentor", mentor.DisplayName);
        Assert.Equal(2, mentor.MentorTopics.Count);
    }

    [Fact]
    public async Task CreateMentor_Fails_WhenPasswordRejected()
    {
        using var db = TestHelpers.NewContext();
        var um = MockUserManager();
        um.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));
        var svc = NewService(db, um.Object);

        var result = await svc.CreateMentorAsync("x@x.com", "weak", "Name", null, Array.Empty<int>());

        Assert.False(result.Success);
        Assert.Contains("Password too weak", result.Error);
        Assert.Equal(0, await db.Mentors.CountAsync());
    }

    [Fact]
    public async Task SetMentorTopics_ReplacesSkillSet()
    {
        using var db = TestHelpers.NewContext();
        var t1 = new Topic { Name = "Math" };
        var t2 = new Topic { Name = "Physics" };
        var t3 = new Topic { Name = "Cooking" };
        db.Topics.AddRange(t1, t2, t3);
        var mentor = new Mentor { DisplayName = "M" };
        mentor.MentorTopics.Add(new MentorTopic { Topic = t1 });
        db.Mentors.Add(mentor);
        await db.SaveChangesAsync();

        var svc = NewService(db);
        await svc.SetMentorTopicsAsync(mentor.Id, new[] { t2.Id, t3.Id });

        var reloaded = await db.Mentors.Include(m => m.MentorTopics).SingleAsync();
        var ids = reloaded.MentorTopics.Select(mt => mt.TopicId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { t2.Id, t3.Id }.OrderBy(x => x).ToList(), ids);
    }
}
