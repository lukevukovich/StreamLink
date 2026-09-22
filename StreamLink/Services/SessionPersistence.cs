using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using StreamLink.Models;

namespace StreamLink.Services;

public sealed class SessionPersistence(IDataProtectionProvider protectionProvider)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("StreamLink.Session.v1");
    private const string StorageKey = "streamlink.session";

    public string StorageKeyName => StorageKey;

    public string Protect(XtreamSession session)
        => protector.Protect(JsonSerializer.Serialize(session));

    public string ProtectString(string value) => protector.Protect(value);

    public string? UnprotectString(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try { return protector.Unprotect(protectedValue); }
        catch (Exception) { return null; }
    }

    public XtreamSession? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try { return JsonSerializer.Deserialize<XtreamSession>(protector.Unprotect(protectedValue)); }
        catch (Exception) { return null; }
    }
}
