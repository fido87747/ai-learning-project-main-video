using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

public record OperationResult(bool Success, string? Error);

// Admin/management operations for mentors, their skills (topics), and the topic catalog.
public class MentorManagementService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public MentorManagementService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // ---- Topics ----

    public async Task<List<Topic>> GetTopicsAsync() =>
        await _db.Topics.OrderBy(t => t.Name).ToListAsync();

    public async Task<OperationResult> AddTopicAsync(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return new OperationResult(false, "Topic name is required.");
        }

        if (await _db.Topics.AnyAsync(t => t.Name.ToLower() == name.ToLower()))
        {
            return new OperationResult(false, "A topic with that name already exists.");
        }

        _db.Topics.Add(new Topic { Name = name });
        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }

    public async Task<OperationResult> DeleteTopicAsync(int topicId)
    {
        var topic = await _db.Topics.FindAsync(topicId);
        if (topic is null)
        {
            return new OperationResult(false, "Topic not found.");
        }

        if (await _db.Bookings.AnyAsync(b => b.TopicId == topicId))
        {
            return new OperationResult(false, "This topic has bookings and cannot be removed.");
        }

        var links = await _db.MentorTopics.Where(mt => mt.TopicId == topicId).ToListAsync();
        _db.MentorTopics.RemoveRange(links);
        _db.Topics.Remove(topic);
        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }

    // ---- Mentors ----

    public async Task<List<Mentor>> GetMentorsAsync() =>
        await _db.Mentors
            .Include(m => m.MentorTopics)
            .ThenInclude(mt => mt.Topic)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();

    public async Task<Mentor?> GetMentorAsync(int id) =>
        await _db.Mentors
            .Include(m => m.MentorTopics)
            .ThenInclude(mt => mt.Topic)
            .FirstOrDefaultAsync(m => m.Id == id);

    public async Task<OperationResult> CreateMentorAsync(
        string email, string password, string displayName, string? bio, IEnumerable<int> topicIds)
    {
        email = (email ?? string.Empty).Trim();
        displayName = (displayName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(email))
            return new OperationResult(false, "Email is required.");
        if (string.IsNullOrWhiteSpace(displayName))
            return new OperationResult(false, "Display name is required.");

        if (await _userManager.FindByEmailAsync(email) is not null)
            return new OperationResult(false, "A user with that email already exists.");

        if (!await _roleManager.RoleExistsAsync(Roles.Mentor))
            await _roleManager.CreateAsync(new IdentityRole(Roles.Mentor));

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = displayName
        };

        var create = await _userManager.CreateAsync(user, password);
        if (!create.Succeeded)
            return new OperationResult(false, string.Join(" ", create.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, Roles.Mentor);

        var mentor = new Mentor
        {
            UserId = user.Id,
            DisplayName = displayName,
            Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim()
        };

        var ids = topicIds?.Distinct().ToList() ?? new List<int>();
        foreach (var tid in ids)
        {
            mentor.MentorTopics.Add(new MentorTopic { TopicId = tid });
        }

        _db.Mentors.Add(mentor);
        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }

    public async Task<OperationResult> UpdateMentorProfileAsync(int mentorId, string displayName, string? bio)
    {
        var mentor = await _db.Mentors.FindAsync(mentorId);
        if (mentor is null)
            return new OperationResult(false, "Mentor not found.");

        displayName = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            return new OperationResult(false, "Display name is required.");

        mentor.DisplayName = displayName;
        mentor.Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }

    // Replaces a mentor's skill set with the provided topic ids.
    public async Task<OperationResult> SetMentorTopicsAsync(int mentorId, IEnumerable<int> topicIds)
    {
        var mentor = await _db.Mentors
            .Include(m => m.MentorTopics)
            .FirstOrDefaultAsync(m => m.Id == mentorId);
        if (mentor is null)
            return new OperationResult(false, "Mentor not found.");

        var desired = topicIds?.Distinct().ToHashSet() ?? new HashSet<int>();

        var toRemove = mentor.MentorTopics.Where(mt => !desired.Contains(mt.TopicId)).ToList();
        foreach (var mt in toRemove)
        {
            mentor.MentorTopics.Remove(mt);
        }

        var existing = mentor.MentorTopics.Select(mt => mt.TopicId).ToHashSet();
        foreach (var tid in desired.Where(id => !existing.Contains(id)))
        {
            mentor.MentorTopics.Add(new MentorTopic { MentorId = mentor.Id, TopicId = tid });
        }

        await _db.SaveChangesAsync();
        return new OperationResult(true, null);
    }
}
