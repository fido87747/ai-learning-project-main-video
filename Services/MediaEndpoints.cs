using MentorBooking.Data;
using Microsoft.EntityFrameworkCore;

namespace MentorBooking.Services;

// Public endpoint that serves a mentor's profile picture from the database.
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/mentors/{id:int}/photo", async (int id, ApplicationDbContext db) =>
        {
            var image = await db.MentorProfileImages
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.MentorId == id);

            if (image is null || image.Content.Length == 0)
            {
                return Results.NotFound();
            }

            return Results.File(image.Content, image.ContentType);
        }).AllowAnonymous();
    }
}
