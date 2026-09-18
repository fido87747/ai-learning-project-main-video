using MentorBooking.Data;
using MentorBooking.Models;
using MentorBooking.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MentorBookingDb;Database=MentorBooking;Trusted_Connection=True;MultipleActiveResultSets=true";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

// Configuration-bound options
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<MeetingSettings>(builder.Configuration.GetSection("Meeting"));

// Application services
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IMeetingService, JitsiMeetingService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<CurrentMentorService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<MentorManagementService>();
builder.Services.AddScoped<MentorProfileService>();

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
    MentorBooking.Services.RevalidatingIdentityAuthenticationStateProvider>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapBlazorHub();

app.MapReportEndpoints();
app.MapMediaEndpoints();

app.MapFallbackToPage("/_Host");

// Apply migrations and seed data on startup, controlled by configuration flags.
var applyMigrations = builder.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true);
var seedDatabase = builder.Configuration.GetValue("Database:SeedOnStartup", true);
await DbSeeder.SeedAsync(app.Services, applyMigrations, seedDatabase);

app.Run();
