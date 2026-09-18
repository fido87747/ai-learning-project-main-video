using MentorBooking.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace MentorBooking.Services;

// Resolves the ApplicationUser for the currently signed-in user (any role).
public class CurrentUserService
{
    private readonly AuthenticationStateProvider _authProvider;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserService(
        AuthenticationStateProvider authProvider,
        UserManager<ApplicationUser> userManager)
    {
        _authProvider = authProvider;
        _userManager = userManager;
    }

    public async Task<ApplicationUser?> GetUserAsync()
    {
        var state = await _authProvider.GetAuthenticationStateAsync();
        if (state.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }
        return await _userManager.GetUserAsync(state.User);
    }
}
