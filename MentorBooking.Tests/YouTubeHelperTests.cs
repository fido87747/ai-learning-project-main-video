using MentorBooking.Services;

namespace MentorBooking.Tests;

public class YouTubeHelperTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=30s", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=10", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void TryGetVideoId_ParsesValidUrls(string url, string expected)
    {
        Assert.True(YouTubeHelper.TryGetVideoId(url, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://vimeo.com/12345678")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    public void TryGetVideoId_RejectsInvalidUrls(string? url)
    {
        Assert.False(YouTubeHelper.TryGetVideoId(url, out var id));
        Assert.Equal(string.Empty, id);
    }

    [Fact]
    public void EmbedUrl_BuildsExpectedUrl()
    {
        Assert.Equal("https://www.youtube.com/embed/dQw4w9WgXcQ",
            YouTubeHelper.EmbedUrl("dQw4w9WgXcQ"));
    }
}
