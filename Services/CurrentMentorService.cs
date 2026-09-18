using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

// Resolves the Mentor entity for the currently signed-in user.
public class CurrentMentorService
{
    private readonly AuthenticationStateProvider _authProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public CurrentMentorService(
        AuthenticationStateProvider authProvider,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db)
    {
        _authProvider = authProvider;
        _userManager = userManager;
        _db = db;
    }

    public async Task<Mentor?> GetAsync()
    {
        var state = await _authProvider.GetAuthenticationStateAsync();
        var userId = _userManager.GetUserId(state.User);
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        return await _db.Mentors
            .Include(m => m.MentorTopics)
            .ThenInclude(mt => mt.Topic)
            .FirstOrDefaultAsync(m => m.UserId == userId);
    }
}
