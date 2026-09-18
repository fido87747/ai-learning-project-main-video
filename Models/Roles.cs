namespace MentorBooking.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Mentor = "Mentor";
    public const string Student = "Student";

    public static readonly string[] All = { Admin, Mentor, Student };
}
