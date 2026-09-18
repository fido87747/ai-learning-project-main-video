using MentorBooking.Data;
using MentorBooking.Models;
using MentorBooking.Services;

namespace MentorBooking.Tests;

public class MentorProfileServiceTests
{
    // Seeds two mentors, each with distinct skills, for public-listing/video tests.
    private static async Task<(Mentor alice, Mentor bob, Topic math, Topic physics, Topic cooking)> SeedAsync(
        ApplicationDbContext db)
    {
        var math = new Topic { Name = "Math" };
        var physics = new Topic { Name = "Physics" };
        var cooking = new Topic { Name = "Cooking" };
        db.Topics.AddRange(math, physics, cooking);
        await db.SaveChangesAsync();

        var alice = new Mentor { DisplayName = "Alice" };
        alice.MentorTopics.Add(new MentorTopic { TopicId = math.Id });
        alice.MentorTopics.Add(new MentorTopic { TopicId = physics.Id });

        var bob = new Mentor { DisplayName = "Bob" };
        bob.MentorTopics.Add(new MentorTopic { TopicId = cooking.Id });

        db.Mentors.AddRange(alice, bob);
        await db.SaveChangesAsync();

        return (alice, bob, math, physics, cooking);
    }

    [Fact]
    public async Task AddVideo_Succeeds_ForOwnSkill_AndParsesId()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, math, _, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        var result = await svc.AddVideoAsync(alice.Id,
            "https://youtu.be/dQw4w9WgXcQ", "Algebra basics", math.Id);

        Assert.True(result.Success);
        Assert.NotNull(result.Video);
        Assert.Equal("dQw4w9WgXcQ", result.Video!.YouTubeId);

        var videos = await svc.GetVideosAsync(alice.Id);
        Assert.Single(videos);
    }

    [Fact]
    public async Task AddVideo_Rejects_SkillNotOwnedByMentor()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, _, _, cooking) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        var result = await svc.AddVideoAsync(alice.Id,
            "https://youtu.be/dQw4w9WgXcQ", "Cooking video", cooking.Id);

        Assert.False(result.Success);
        Assert.Contains("your own skillsets", result.Error);
        Assert.Empty(await svc.GetVideosAsync(alice.Id));
    }

    [Fact]
    public async Task AddVideo_Rejects_InvalidUrl_AndMissingDescription()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, math, _, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        var badUrl = await svc.AddVideoAsync(alice.Id, "https://vimeo.com/123", "desc", math.Id);
        Assert.False(badUrl.Success);
        Assert.Contains("valid YouTube", badUrl.Error);

        var noDesc = await svc.AddVideoAsync(alice.Id, "https://youtu.be/dQw4w9WgXcQ", "  ", math.Id);
        Assert.False(noDesc.Success);
        Assert.Contains("description is required", noDesc.Error);
    }

    [Fact]
    public async Task UpdateAndDeleteVideo_Work()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, math, physics, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        var added = await svc.AddVideoAsync(alice.Id, "https://youtu.be/dQw4w9WgXcQ", "Old", math.Id);
        var updated = await svc.UpdateVideoAsync(added.Video!.Id, alice.Id,
            "https://www.youtube.com/watch?v=kKKM8Y-u7ds", "New description", physics.Id);

        Assert.True(updated.Success);
        Assert.Equal("kKKM8Y-u7ds", updated.Video!.YouTubeId);
        Assert.Equal(physics.Id, updated.Video.TopicId);
        Assert.Equal("New description", updated.Video.Description);

        var deleted = await svc.DeleteVideoAsync(added.Video.Id, alice.Id);
        Assert.True(deleted.Success);
        Assert.Empty(await svc.GetVideosAsync(alice.Id));
    }

    [Fact]
    public async Task DeleteVideo_Rejects_OtherMentorsVideo()
    {
        using var db = TestHelpers.NewContext();
        var (alice, bob, math, _, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        var added = await svc.AddVideoAsync(alice.Id, "https://youtu.be/dQw4w9WgXcQ", "Alice's", math.Id);

        var result = await svc.DeleteVideoAsync(added.Video!.Id, bob.Id);
        Assert.False(result.Success);
        Assert.Single(await svc.GetVideosAsync(alice.Id));
    }

    [Fact]
    public async Task GetVideos_AreOrderedByTopicThenDescription()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, math, physics, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        await svc.AddVideoAsync(alice.Id, "https://youtu.be/dQw4w9WgXcQ", "Zebra", physics.Id);
        await svc.AddVideoAsync(alice.Id, "https://youtu.be/kKKM8Y-u7ds", "Beta", math.Id);
        await svc.AddVideoAsync(alice.Id, "https://youtu.be/rfG8ce4nNh0", "Alpha", math.Id);

        var videos = await svc.GetVideosAsync(alice.Id);

        // Math (alphabetically before Physics) first, then by description within the group.
        Assert.Collection(videos,
            v => Assert.Equal("Alpha", v.Description),
            v => Assert.Equal("Beta", v.Description),
            v => Assert.Equal("Zebra", v.Description));
    }

    [Fact]
    public async Task GetPublicMentors_AreAlphabetical_AndFilterByVideos()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, math, _, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        // Only Alice has a video.
        await svc.AddVideoAsync(alice.Id, "https://youtu.be/dQw4w9WgXcQ", "Algebra", math.Id);

        var all = await svc.GetPublicMentorsAsync(includeWithoutVideos: true);
        Assert.Equal(new[] { "Alice", "Bob" }, all.Select(m => m.DisplayName).ToArray());

        var withVideos = await svc.GetPublicMentorsAsync(includeWithoutVideos: false);
        Assert.Single(withVideos);
        Assert.Equal("Alice", withVideos[0].DisplayName);

        var counts = await svc.GetVideoCountsAsync();
        Assert.Equal(1, counts[alice.Id]);
    }

    [Fact]
    public async Task SetProfileImage_Validates_AndStoresAndReplaces()
    {
        using var db = TestHelpers.NewContext();
        var (alice, _, _, _, _) = await SeedAsync(db);
        var svc = TestHelpers.NewProfileService(db);

        Assert.False(await svc.HasProfileImageAsync(alice.Id));

        var tooBig = await svc.SetProfileImageAsync(alice.Id,
            new byte[MentorProfileService.MaxImageBytes + 1], "image/png");
        Assert.False(tooBig.Success);

        var badType = await svc.SetProfileImageAsync(alice.Id, new byte[] { 1, 2, 3 }, "application/pdf");
        Assert.False(badType.Success);

        var ok = await svc.SetProfileImageAsync(alice.Id, new byte[] { 1, 2, 3 }, "image/png");
        Assert.True(ok.Success);
        Assert.True(await svc.HasProfileImageAsync(alice.Id));

        var stored = await svc.GetProfileImageAsync(alice.Id);
        Assert.Equal("image/png", stored!.ContentType);
        Assert.Equal(3, stored.Content.Length);

        // Re-uploading replaces the existing image (still one row).
        var replace = await svc.SetProfileImageAsync(alice.Id, new byte[] { 9, 9 }, "image/jpeg");
        Assert.True(replace.Success);
        var updated = await svc.GetProfileImageAsync(alice.Id);
        Assert.Equal("image/jpeg", updated!.ContentType);
        Assert.Equal(2, updated.Content.Length);
    }
}
