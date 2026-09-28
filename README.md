# StreamLink

StreamLink is a self-hosted Blazor app for browsing live IPTV channels from an Xtream Codes-compatible provider and handing direct provider links to external players. **It does not play, transcode, relay, proxy, or serve video.** Use only accounts and streams you are authorized to access.

## Requirements and setup

- .NET 8 SDK, a modern browser with JavaScript and local storage, and an Xtream-compatible provider account.
- An external player (VLC recommended) installed on devices where you want to watch.

From the repository root:

```powershell
dotnet restore .\StreamLink\StreamLink.csproj
dotnet run --project .\StreamLink\StreamLink.csproj --urls "http://localhost:5000"
```

For other devices, make the app accessible from their network. Use HTTPS before exposing it outside a trusted local network. Sign in with the provider URL, username and password, then browse **Live TV** and select a channel. The actions first ask the provider for a redirect from its HLS `.m3u8` URL (or `.ts` URL if HLS is unavailable). If there is no redirect, or the redirect visibly contains your username or password, the action fails rather than handing out the original URL. A browser may download the redirected playlist instead of playing it; that is expected. The server reads only the HTTP redirect response headers, never video content. The _device running the player_ must be able to reach the redirected URL.

The single VLC action adapts to the device: **Play in VLC** attempts to launch VLC on iOS/iPadOS using its `vlc-x-callback` scheme and on Android using a browser intent. VLC must be installed and its handler registered on that device; some browsers or platforms will reject the launch. On desktop it reads **Download for VLC** and downloads a small `.m3u` playlist containing the resolved URL; open it with VLC. Unknown platforms also download the playlist. If VLC does not open on mobile, use **Copy link** and VLC's **Open Network Stream**. The playlist contains only the resolved URL; StreamLink does not serve or download video. **Copy link** resolves a fresh redirect, then shows the link in a selectable input and attempts to copy it automatically. Browsers may deny clipboard access, especially on mobile over HTTP or after an asynchronous request; tap and hold the input to copy manually. A failed redirect produces an error and no copied link or playlist. Some providers may not redirect `.m3u8` or may restrict connections, location or codecs.

## Shares and credentials: important

**A direct Xtream URL often includes the username and password in its path.** StreamLink now requires a provider redirect for the watch and channel actions, and rejects destinations visibly containing those credentials. This does not prove the destination is safe: a signed URL remains a bearer secret, may grant access during its lifetime, and a provider could encode credentials in an unfamiliar way. A valid `/watch/{code}` lets its holder request that redirect. The page does not stream video from StreamLink.

Share codes are random and expire after a chosen lifetime (1–24 hours, default 4 hours); the owner can revoke them on **Shares**. Creating another link for the same channel reuses its active share and original expiry. Each action checks that the share is still active. Revocation cannot invalidate a redirected URL already saved or copied; provider expiration governs that URL. Old shared direct URLs may already be in circulation. Use a separate limited provider account if available, and rotate its credentials if a direct link was previously exposed.

Treat direct URLs and downloaded `.m3u` launcher files like passwords. Delete downloaded playlists when finished. Avoid posting links publicly, sending them over HTTP, or including them in screenshots or logs. External players and browsers may retain URL history. The app uses `no-referrer` and `no-store` headers for its pages, but cannot control what the provider, browser, or external player records. The provider may use HTTP even if the app itself uses HTTPS, so check provider transport before sharing sensitive links.

## Data and configuration

The app stores shares in SQLite (`ConnectionStrings:StreamLink`, default `Data Source=streamlink.db`). Share provider URLs are protected at rest using ASP.NET Core Data Protection; keys are under `StreamLink/App_Data/keys`. The browser stores a server-protected session in local storage. Keep the database and keys private and stable across deployments; losing keys can invalidate saved sessions/shares. This protection **does not** hide URLs from anyone opening a valid share page. No FFmpeg installation or configuration is used.

Build with `dotnet build .\StreamLink\StreamLink.csproj`. The app performs provider API requests for login and channel listings, but has no media playback libraries or `/stream/` endpoints. Old proxy URLs stop working after upgrading. Provider availability, output formats and codec support remain outside StreamLink's control.
