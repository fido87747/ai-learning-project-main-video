using System.ComponentModel.DataAnnotations;

namespace MentorBooking.Models;

public class Booking
{
    public int Id { get; set; }

    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;

    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;

    // 1-hour slot stored in UTC.
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    [Required]
    [StringLength(150)]
    public string StudentName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string StudentEmail { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Message { get; set; } = string.Empty;

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    [StringLength(2000)]
    public string? RejectionReason { get; set; }

    [StringLength(2000)]
    public string? CancellationReason { get; set; }

    public string? MeetingUrl { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
