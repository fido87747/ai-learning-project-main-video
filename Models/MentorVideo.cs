using System.ComponentModel.DataAnnotations;

namespace MentorBooking.Models;

// A public YouTube video shared by a mentor, tagged with a single skillset (Topic)
// so the public mentor page can group videos by skillset.
public class MentorVideo
{
    public int Id { get; set; }

    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;

    // The skillset this video belongs to (one per video). Must be one of the mentor's skills.
    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;

    [Required]
    [StringLength(500)]
    public string Url { get; set; } = string.Empty;

    // Canonical 11-char YouTube video id parsed from Url; used to build embed/thumbnail URLs.
    [Required]
    [StringLength(20)]
    public string YouTubeId { get; set; } = string.Empty;

    [Required]
    [StringLength(300)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
