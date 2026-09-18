namespace MentorBooking.Services;

public class MeetingSettings
{
    // Base URL for the Jitsi Meet server. The public server is free and needs no account.
    public string JitsiBaseUrl { get; set; } = "https://meet.jit.si";
}

public interface IMeetingService
{
    string CreateMeetingUrl(int bookingId);
}

// Generates a unique Jitsi Meet room URL per booking. No API keys required.
public class JitsiMeetingService : IMeetingService
{
    private readonly MeetingSettings _settings;

    public JitsiMeetingService(Microsoft.Extensions.Options.IOptions<MeetingSettings> settings)
    {
        _settings = settings.Value;
    }

    public string CreateMeetingUrl(int bookingId)
    {
        var room = $"MentorBooking-{bookingId}-{Guid.NewGuid():N}";
        return $"{_settings.JitsiBaseUrl.TrimEnd('/')}/{room}";
    }
}
