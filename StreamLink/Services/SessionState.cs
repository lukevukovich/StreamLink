using StreamLink.Models;
using System.Security.Cryptography;
using System.Text;

namespace StreamLink.Services;

public sealed class SessionState
{
    public event Action? Changed;
    public XtreamSession? Session { get; private set; }
    public bool IsHydrated { get; private set; }
    public string OwnerKey { get; private set; } = Guid.NewGuid().ToString("N");
    public bool IsAuthenticated => Session is not null;

    public void MarkHydrated()
    {
        IsHydrated = true;
        Changed?.Invoke();
    }

    public void SetSession(XtreamSession session)
    {
        Session = session;
        OwnerKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.ServerUrl}\n{session.Username}"))).ToLowerInvariant();
        Changed?.Invoke();
    }

    public void Clear()
    {
        Session = null;
        IsHydrated = true;
        Changed?.Invoke();
    }
}
