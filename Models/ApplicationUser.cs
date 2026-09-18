using Microsoft.AspNetCore.Identity;

namespace MentorBooking.Models;

// Identity user for Mentors and Admins. Students are anonymous and do not have accounts.
public class ApplicationUser : IdentityUser
{
    [System.ComponentModel.DataAnnotations.StringLength(150)]
    public string? FullName { get; set; }

    // Prototype-only: a plaintext password hint shown on the login page for SEEDED demo
    // accounts. Left null for admin-created mentors (their real password is never stored).
    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string? DemoPasswordHint { get; set; }
}
