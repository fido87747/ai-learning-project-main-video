using System.ComponentModel.DataAnnotations;

namespace MentorBooking.Models;

public class Topic
{
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public ICollection<MentorTopic> MentorTopics { get; set; } = new List<MentorTopic>();
}
