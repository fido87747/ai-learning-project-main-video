using System.ComponentModel.DataAnnotations;
using System.Text;
using MentorBooking.Models;
using MentorBooking.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace MentorBooking.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender)
    {
        _userManager = userManager;
        _emailSender = emailSender;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool LinkGenerated { get; set; }
    public bool AccountNotFound { get; set; }
    public string? ResetUrl { get; set; }
    public string? EmailBody { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            AccountNotFound = true;
            return Page();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        ResetUrl = Url.Page(
            "/Account/ResetPassword",
            pageHandler: null,
            values: new { email = Input.Email, token = encodedToken },
            protocol: Request.Scheme);

        EmailBody =
            $"Hello {(string.IsNullOrWhiteSpace(user.FullName) ? Input.Email : user.FullName)},\n\n" +
            "We received a request to reset the password for your Mentor Booking account.\n\n" +
            "Use the link below to choose a new password:\n" +
            $"{ResetUrl}\n\n" +
            "If you did not request a password reset, you can safely ignore this email.\n\n" +
            "— Mentor Booking";

        // Email is simulated (log-only) unless SMTP is configured; the link is also shown on screen.
        await _emailSender.SendAsync(Input.Email, user.FullName ?? Input.Email,
            "Reset your Mentor Booking password", TextToHtml(EmailBody));

        LinkGenerated = true;
        return Page();
    }

    private static string TextToHtml(string text) =>
        System.Net.WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br />");
}
