using System.ComponentModel.DataAnnotations;

namespace MentorBooking.Models;

// A mentor's profile picture, stored in the database (1:1 with Mentor) and served
// via the /mentors/{id}/photo endpoint. Kept in its own table so listing mentors
// never loads the image bytes.
public class MentorProfileImage
{
    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;

    [Required]
    public byte[] Content { get; set; } = Array.Empty<byte>();

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; }
}
