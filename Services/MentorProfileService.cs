using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

public record VideoResult(bool Success, string? Error, MentorVideo? Video = null);

// Mentor profile picture storage and public-video management, plus the public read
// queries used by the anonymous Mentors pages. Reuses OperationResult from
// MentorManagementService for simple success/error outcomes.
public class MentorProfileService
{
    private readonly ApplicationDbContext _db;

    public static readonly string[] AllowedImageContentTypes = { "image/png", "image/jpeg", "image/webp" };
    public const long MaxImageBytes = 2 * 1024 * 1024;

    public MentorProfileService(ApplicationDbContext db) => _db = db;

    // ---- Profile image ----

    public async Task<OperationResult> SetProfileImageAsync(int mentorId, byte[] content, string contentType)
    {
        if (content is null || content.Length == 0)
            return new OperationResult(false, "The uploaded file is empty.");

        if (content.Length > MaxImageBytes)
            return new OperationResult(false, "Image must be 2 MB or smaller.");

        contentType = (contentType ?? string.Empty).ToLowerInvariant();
        if (!AllowedImageContentTypes.Contains(contentType))
            return new OperationResult(false, "Only PNG, JPEG, or WebP images are allowed.");

        if (!await _db.Mentors.AnyAsync(m => m.Id == mentorId))
            return new OperationResult(false, "Mentor not found.");

        var image = await _db.MentorProfileImages.FirstOrDefaultAsync(p => p.MentorId == mentorId);
        if (image is null)
        {
            image = new MentorProfileImage { MentorId = mentorId };
            _db.MentorProfileImages.Add(image);
        }

        image.Content = content;
        image.ContentType = contentType;
        image.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }

    public async Task<MentorProfileImage?> GetProfileImageAsync(int mentorId) =>
        await _db.MentorProfileImages.AsNoTracking().FirstOrDefaultAsync(p => p.MentorId == mentorId);

    public async Task<bool> HasProfileImageAsync(int mentorId) =>
        await _db.MentorProfileImages.AnyAsync(p => p.MentorId == mentorId);

    // ---- Videos (mentor management) ----

    // A mentor's videos ordered for grouping: by skillset name, then description.
    public async Task<List<MentorVideo>> GetVideosAsync(int mentorId) =>
        await _db.MentorVideos
            .Include(v => v.Topic)
            .Where(v => v.MentorId == mentorId)
            .OrderBy(v => v.Topic.Name)
            .ThenBy(v => v.Description)
            .ToListAsync();

    public async Task<VideoResult> AddVideoAsync(int mentorId, string? url, string? description, int topicId)
    {
        var validation = await ValidateVideoInputAsync(mentorId, url, description, topicId);
        if (validation.Error is not null)
            return new VideoResult(false, validation.Error);

        var video = new MentorVideo
        {
            MentorId = mentorId,
            TopicId = topicId,
            Url = validation.Url,
            YouTubeId = validation.VideoId,
            Description = validation.Description,
            CreatedUtc = DateTime.UtcNow
        };

        _db.MentorVideos.Add(video);
        await _db.SaveChangesAsync();
        return new VideoResult(true, null, video);
    }

    public async Task<VideoResult> UpdateVideoAsync(int videoId, int mentorId, string? url, string? description, int topicId)
    {
        var video = await _db.MentorVideos.FirstOrDefaultAsync(v => v.Id == videoId && v.MentorId == mentorId);
        if (video is null)
            return new VideoResult(false, "Video not found.");

        var validation = await ValidateVideoInputAsync(mentorId, url, description, topicId);
        if (validation.Error is not null)
            return new VideoResult(false, validation.Error);

        video.Url = validation.Url;
        video.YouTubeId = validation.VideoId;
        video.Description = validation.Description;
        video.TopicId = topicId;
        await _db.SaveChangesAsync();
        return new VideoResult(true, null, video);
    }

    public async Task<OperationResult> DeleteVideoAsync(int videoId, int mentorId)
    {
        var video = await _db.MentorVideos.FirstOrDefaultAsync(v => v.Id == videoId && v.MentorId == mentorId);
        if (video is null)
            return new OperationResult(false, "Video not found.");

        _db.MentorVideos.Remove(video);
        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }

    private async Task<(string? Error, string Url, string VideoId, string Description)> ValidateVideoInputAsync(
        int mentorId, string? url, string? description, int topicId)
    {
        description = (description ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(description))
            return ("A description is required.", string.Empty, string.Empty, string.Empty);

        if (!YouTubeHelper.TryGetVideoId(url, out var videoId))
            return ("Enter a valid YouTube video URL.", string.Empty, string.Empty, string.Empty);

        // The skillset must be one of the mentor's own skills.
        if (!await _db.MentorTopics.AnyAsync(mt => mt.MentorId == mentorId && mt.TopicId == topicId))
            return ("Choose one of your own skillsets for this video.", string.Empty, string.Empty, string.Empty);

        return (null, url!.Trim(), videoId, description);
    }

    // ---- Public read (anonymous Mentors pages) ----

    // All mentors ordered alphabetically by display name. Optionally only those with videos.
    public async Task<List<Mentor>> GetPublicMentorsAsync(bool includeWithoutVideos)
    {
        var query = _db.Mentors
            .AsNoTracking()
            .Include(m => m.MentorTopics).ThenInclude(mt => mt.Topic)
            .AsQueryable();

        if (!includeWithoutVideos)
            query = query.Where(m => _db.MentorVideos.Any(v => v.MentorId == m.Id));

        return await query.OrderBy(m => m.DisplayName).ToListAsync();
    }

    public async Task<Mentor?> GetPublicMentorAsync(int mentorId) =>
        await _db.Mentors
            .AsNoTracking()
            .Include(m => m.MentorTopics).ThenInclude(mt => mt.Topic)
            .FirstOrDefaultAsync(m => m.Id == mentorId);

    // Convenience for the public list: which mentor ids have at least one video.
    public async Task<HashSet<int>> GetMentorIdsWithVideosAsync()
    {
        var ids = await _db.MentorVideos.Select(v => v.MentorId).Distinct().ToListAsync();
        return ids.ToHashSet();
    }

    // Number of videos per mentor, for the public list.
    public async Task<Dictionary<int, int>> GetVideoCountsAsync()
    {
        var counts = await _db.MentorVideos
            .GroupBy(v => v.MentorId)
            .Select(g => new { MentorId = g.Key, Count = g.Count() })
            .ToListAsync();
        return counts.ToDictionary(x => x.MentorId, x => x.Count);
    }
}
