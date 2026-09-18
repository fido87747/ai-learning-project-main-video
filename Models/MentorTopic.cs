namespace MentorBooking.Models;

// Join entity: a mentor's skill in a given topic (many-to-many Mentor <-> Topic).
public class MentorTopic
{
    public int MentorId { get; set; }
    public Mentor Mentor { get; set; } = null!;

    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
}
