namespace StreamLink.Api;

/// <summary>Provider credentials used to request an API session.</summary>
public sealed record CreateSessionRequest(string ServerUrl, string Username, string Password);

/// <summary>A short-lived bearer token and its UTC expiration.</summary>
public sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt, AccountResponse Account);

/// <summary>Provider account status without credentials.</summary>
public sealed record AccountResponse(string Username, string? Status, DateTimeOffset? ExpiresAt, bool IsTrial,
    int ActiveConnections, int MaxConnections, IReadOnlyList<string> AllowedOutputFormats);

/// <summary>A live television category.</summary>
public sealed record CategoryResponse(string Id, string Name);

/// <summary>A live television channel.</summary>
public sealed record ChannelResponse(int StreamId, string Name, string? CategoryId, string? LogoUrl, string? EpgChannelId);

/// <summary>Identifies a provider channel and requested share lifetime in hours (1–24).</summary>
public sealed record CreateShareRequest(int StreamId, string? CategoryId, int ExpirationHours = 4);

/// <summary>Sets a share's new lifetime in hours (1–24), measured from now.</summary>
public sealed record UpdateShareRequest(int ExpirationHours);

/// <summary>An active share. The code and watch path are bearer secrets.</summary>
public sealed record ShareResponse(string Code, string WatchPath, int StreamId, string ChannelName, string CategoryName,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
