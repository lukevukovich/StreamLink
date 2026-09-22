namespace StreamLink.Models;

public sealed class ShareLink
{
    public int Id { get; set; }
    public string Token { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int StreamId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public bool Revoked { get; set; }
    public string OwnerKey { get; set; } = "";
    public string? ProtectedHlsUrl { get; set; }
}

public sealed record SharePlayback(ShareLink Link, string StreamUrl);

public enum ShareExpiration
{
    EightHours = 8
}
