using System.ComponentModel.DataAnnotations;
using MentorBooking.Data;
using MentorBooking.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _db;

    public LoginModel(SignInManager<ApplicationUser> signInManager, ApplicationDbContext db)
    {
        _signInManager = signInManager;
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public List<DemoAccount> DemoAccounts { get; set; } = new();

    public record DemoAccount(string Role, string Email, string? PasswordHint);

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        await LoadDemoAccountsAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= "/";
        await LoadDemoAccountsAsync();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            return LocalRedirect(returnUrl);
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        return Page();
    }

    private async Task LoadDemoAccountsAsync()
    {
        // Join users to their roles so every mentor/admin account is listed, ordered admins first.
        // Only list seeded demo accounts (those with a known password hint),
        // so self-registered student accounts are not exposed here.
        var query =
            from user in _db.Users
            where user.DemoPasswordHint != null
            join userRole in _db.UserRoles on user.Id equals userRole.UserId
            join role in _db.Roles on userRole.RoleId equals role.Id
            orderby role.Name descending, user.Email
            select new DemoAccount(role.Name!, user.Email!, user.DemoPasswordHint);

        DemoAccounts = await query.ToListAsync();
    }
}
