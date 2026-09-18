using MentorBooking.Models;
using MentorBooking.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        bool applyMigrations = true,
        bool seedData = true)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ApplicationDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        if (applyMigrations)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogInformation(
                "Skipping database migrations on startup (Database:ApplyMigrationsOnStartup=false).");
        }

        if (!seedData)
        {
            logger.LogInformation(
                "Skipping database seeding on startup (Database:SeedOnStartup=false).");
            return;
        }

        // Roles
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Admin user
        var admin = await EnsureUserAsync(userManager, "admin@mentorbooking.local", "Admin User", "Admin#12345", Roles.Admin);

        // Demo student account (students can also register their own accounts).
        await EnsureUserAsync(userManager, "student@mentorbooking.local", "Sam Student", "Student#12345", Roles.Student);

        // Topics
        var topicNames = new[] { "Math", "Physics", "Chemistry", "Biology", "Cooking" };
        foreach (var name in topicNames)
        {
            if (!await db.Topics.AnyAsync(t => t.Name == name))
            {
                db.Topics.Add(new Topic { Name = name });
            }
        }
        await db.SaveChangesAsync();

        var topics = await db.Topics.ToDictionaryAsync(t => t.Name, t => t);

        // Sample mentors, each with an Identity account and skills.
        if (!await db.Mentors.AnyAsync())
        {
            var aliceUser = await EnsureUserAsync(userManager, "alice@mentorbooking.local", "Alice Johnson", "Mentor#12345", Roles.Mentor);
            var bobUser = await EnsureUserAsync(userManager, "bob@mentorbooking.local", "Bob Smith", "Mentor#12345", Roles.Mentor);

            var alice = new Mentor
            {
                UserId = aliceUser.Id,
                DisplayName = "Alice Johnson",
                Bio = "Mathematics and physics tutor with 8 years of experience.",
                MentorTopics = new List<MentorTopic>
                {
                    new() { TopicId = topics["Math"].Id },
                    new() { TopicId = topics["Physics"].Id }
                },
                AvailabilityWindows = new List<AvailabilityWindow>
                {
                    new() { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(12, 0) },
                    new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(17, 0) }
                }
            };

            var bob = new Mentor
            {
                UserId = bobUser.Id,
                DisplayName = "Bob Smith",
                Bio = "Chemistry, biology, and enthusiastic home cook.",
                MentorTopics = new List<MentorTopic>
                {
                    new() { TopicId = topics["Chemistry"].Id },
                    new() { TopicId = topics["Biology"].Id },
                    new() { TopicId = topics["Cooking"].Id }
                },
                AvailabilityWindows = new List<AvailabilityWindow>
                {
                    new() { DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(14, 0) },
                    new() { DayOfWeek = DayOfWeek.Thursday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(12, 0) }
                }
            };

            db.Mentors.AddRange(alice, bob);
            await db.SaveChangesAsync();
        }

        // Sample public videos for the demo mentors (idempotent; only tagged with a
        // skillset the mentor actually has).
        if (!await db.MentorVideos.AnyAsync())
        {
            var mentorsByName = await db.Mentors
                .Include(m => m.MentorTopics)
                .ToDictionaryAsync(m => m.DisplayName, m => m);

            void AddVideo(string mentorName, string topicName, string url, string description)
            {
                if (mentorsByName.TryGetValue(mentorName, out var mentor)
                    && topics.TryGetValue(topicName, out var topic)
                    && mentor.MentorTopics.Any(mt => mt.TopicId == topic.Id)
                    && YouTubeHelper.TryGetVideoId(url, out var videoId))
                {
                    db.MentorVideos.Add(new MentorVideo
                    {
                        MentorId = mentor.Id,
                        TopicId = topic.Id,
                        Url = url,
                        YouTubeId = videoId,
                        Description = description,
                        CreatedUtc = DateTime.UtcNow
                    });
                }
            }

            AddVideo("Alice Johnson", "Math", "https://www.youtube.com/watch?v=WUvTyaaNkzM",
                "Understanding derivatives from first principles.");
            AddVideo("Alice Johnson", "Math", "https://youtu.be/rfG8ce4nNh0",
                "Solving quadratic equations step by step.");
            AddVideo("Alice Johnson", "Physics", "https://www.youtube.com/watch?v=kKKM8Y-u7ds",
                "Newton's three laws of motion explained.");
            AddVideo("Bob Smith", "Chemistry", "https://www.youtube.com/watch?v=rz4Dd1I_fX0",
                "The periodic table for absolute beginners.");
            AddVideo("Bob Smith", "Cooking", "https://www.youtube.com/watch?v=JMA2SqaHgjA",
                "Essential knife skills for the home cook.");

            await db.SaveChangesAsync();
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string fullName,
        string password,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = fullName,
                DemoPasswordHint = password
            };
            await userManager.CreateAsync(user, password);
        }
        else
        {
            if (user.DemoPasswordHint != password)
            {
                user.DemoPasswordHint = password;
                await userManager.UpdateAsync(user);
            }

            // Keep the seeded demo account's password in sync with the displayed hint,
            // in case it drifted (e.g. via a password reset/change during testing).
            if (!await userManager.CheckPasswordAsync(user, password))
            {
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                await userManager.ResetPasswordAsync(user, token, password);
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }

        return user;
    }
}
