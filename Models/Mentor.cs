using System.ComponentModel.DataAnnotations;

namespace MentorBooking.Models;

public class Mentor
{
    public int Id { get; set; }

    // Links to the ASP.NET Core Identity user (Mentor role). Nullable for seed/demo mentors.
    public string? UserId { get; set; }

    [Required]
    [StringLength(150)]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Bio { get; set; }

    public ICollection<MentorTopic> MentorTopics { get; set; } = new List<MentorTopic>();

    public ICollection<AvailabilityWindow> AvailabilityWindows { get; set; } = new List<AvailabilityWindow>();

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();

    public ICollection<MentorVideo> Videos { get; set; } = new List<MentorVideo>();

    // Profile picture (stored in the DB); null when the mentor hasn't uploaded one.
    public MentorProfileImage? ProfileImage { get; set; }
}
