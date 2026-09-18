using MentorBooking.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Mentor> Mentors => Set<Mentor>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<MentorTopic> MentorTopics => Set<MentorTopic>();
    public DbSet<AvailabilityWindow> AvailabilityWindows => Set<AvailabilityWindow>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<MentorVideo> MentorVideos => Set<MentorVideo>();
    public DbSet<MentorProfileImage> MentorProfileImages => Set<MentorProfileImage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MentorTopic>()
            .HasKey(mt => new { mt.MentorId, mt.TopicId });

        builder.Entity<MentorTopic>()
            .HasOne(mt => mt.Mentor)
            .WithMany(m => m.MentorTopics)
            .HasForeignKey(mt => mt.MentorId);

        builder.Entity<MentorTopic>()
            .HasOne(mt => mt.Topic)
            .WithMany(t => t.MentorTopics)
            .HasForeignKey(mt => mt.TopicId);

        builder.Entity<Topic>()
            .HasIndex(t => t.Name)
            .IsUnique();

        builder.Entity<Booking>()
            .HasOne(b => b.Mentor)
            .WithMany(m => m.Bookings)
            .HasForeignKey(b => b.MentorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Booking>()
            .HasOne(b => b.Topic)
            .WithMany()
            .HasForeignKey(b => b.TopicId)
            .OnDelete(DeleteBehavior.Restrict);

        // Prevent two active bookings for the same mentor at the same start time.
        // Application logic further restricts this to Pending/Confirmed statuses.
        builder.Entity<Booking>()
            .HasIndex(b => new { b.MentorId, b.StartUtc });

        // A mentor's shared videos, each tagged with a single skillset (Topic).
        builder.Entity<MentorVideo>(e =>
        {
            e.HasOne(v => v.Mentor)
                .WithMany(m => m.Videos)
                .HasForeignKey(v => v.MentorId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(v => v.Topic)
                .WithMany()
                .HasForeignKey(v => v.TopicId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(v => new { v.MentorId, v.TopicId });
        });

        // A mentor's profile picture (1:1), keyed by MentorId.
        builder.Entity<MentorProfileImage>(e =>
        {
            e.HasKey(p => p.MentorId);
            e.HasOne(p => p.Mentor)
                .WithOne(m => m.ProfileImage)
                .HasForeignKey<MentorProfileImage>(p => p.MentorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
