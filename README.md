# StreamLink

StreamLink is a self-hosted web app for browsing live IPTV channels from an Xtream Codes-compatible provider, watching streams in a browser, and sharing expiring channel links.

> Use StreamLink only with provider accounts and streams you are authorized to access. StreamLink does not provide IPTV subscriptions or channel content.

## Features

- Sign in with a provider server URL, username, and password.
- Browse live-TV categories and channels, with search.
- Play provider streams using HLS or MPEG-TS.
- Optionally transcode live MPEG-TS with FFmpeg to browser-friendly H.264/AAC when direct playback is incompatible.
- Create eight-hour share links, copy/open/revoke them, and open a shared link by URL or code.
- Display account and share expiry times in the viewer's browser timezone.
- Store the session encrypted in browser local storage and protect share URLs at rest.
- Proxy manifests and stream resources through the app to avoid exposing provider URLs directly to the playback page.

## Requirements

- .NET 8 SDK.
- A reachable Xtream Codes-compatible provider account that permits live streams.
- A modern browser with JavaScript and local storage enabled.
- FFmpeg is optional for direct playback, but recommended when the browser cannot decode the provider stream. The current transcoder uses `libx264` and AAC, so install a build that includes those encoders.

## Run locally

From the repository root:

```powershell
dotnet restore .\StreamLink\StreamLink.csproj
dotnet run --project .\StreamLink\StreamLink.csproj --urls "http://localhost:5000"
```

Or open `StreamLink/StreamLink.csproj` in Visual Studio or VS Code and run the ASP.NET Core project. The launch profiles are in `StreamLink/Properties/launchSettings.json`.

For access from another device on your network, bind to an available interface, for example `http://0.0.0.0:5000`, and connect using the host machine's LAN address. Use HTTPS and suitable network controls before exposing the app beyond a trusted development network.

Open the app, enter the provider base URL (including `https://` or `http://`), username, and password, then choose **Connect securely**.

## FFmpeg playback

Playback tries provider HLS first, then the original MPEG-TS source. If neither produces playable video, it automatically tries the server-side FFmpeg compatibility transcode (when available). The player shows which playback method is currently being tried. The transcode:

- Reads the upstream TS stream through a pipe; provider credentials are not passed as FFmpeg command-line arguments.
- Encodes video as H.264 (`libx264`, CRF 18, `superfast`) and audio as AAC.
- Preserves source resolution and frame rate, with a peak video-rate cap of 10 Mbps.
- Uses CPU and allows up to two simultaneous transcodes per app process. Additional transcode requests receive HTTP 503 until a slot is available.

Install FFmpeg and make `ffmpeg` available to the StreamLink process on `PATH`, then restart StreamLink. On Windows, if the process does not inherit the updated user `PATH`, set the executable path in configuration instead:

```json
{
  "FFmpeg": {
    "Path": "C:\\path\\to\\ffmpeg.exe"
  }
}
```

The app reads this from normal ASP.NET Core configuration, so it can be supplied via `appsettings.json`, environment variables such as `FFmpeg__Path`, or another configured provider. Do not commit machine-specific paths or secrets.

Transcoding may improve codec compatibility, but cannot recover detail missing from the source, bypass DRM, fix an offline provider, or guarantee no buffering. Quality and smoothness depend on source quality, CPU throughput, and network capacity. Direct HLS/TS playback does not consume an FFmpeg transcode slot.

## Configuration and data

Configuration is provided by ASP.NET Core's standard configuration system. The included `StreamLink/appsettings.json` contains:

- `ConnectionStrings:StreamLink`: SQLite connection string. Defaults to `Data Source=streamlink.db`.
- `Logging`: application log levels.
- `FFmpeg:Path` (optional): explicit FFmpeg executable path.

On startup, the app ensures the SQLite schema exists. Share records are stored in SQLite. Data Protection keys are persisted under `StreamLink/App_Data/keys` and use the application name `StreamLink`; keep these keys stable and private across restarts/deployments. Losing or replacing the keys can invalidate protected session/share data.

The provider session is saved in the visitor's browser local storage using ASP.NET Core Data Protection. Share stream URLs are protected before being stored. Keep the app's key directory private, restrict access to the app and its database, use HTTPS on untrusted networks, and avoid sharing application logs that may include infrastructure details. Provider credentials are necessarily used to construct upstream requests; the app suppresses HTTP-client URL logging for the provider/proxy clients.

## Using StreamLink

1. Sign in with your provider details.
2. Open **Live TV**, choose a category, and select a channel.
3. The player tries HLS, then direct TS, and falls back to FFmpeg if needed. The method shown beside **LIVE** updates as playback switches. Use browser-native controls for play, volume, and fullscreen. **Open in external player** opens the app's proxied stream URL in another player.
4. Use **Choose a format** to view provider HLS/TS links when those formats are allowed by the account.
5. Select **Share** to create an eight-hour link. Manage active links under **Shares**, where they can be copied, opened, or revoked.
6. A recipient can open the share URL directly or paste its URL/code into **Watch**. The watch page shows when the share expires in the recipient's local timezone.
7. **Account** shows provider status, connection limits, trial status, and the account expiry in the local timezone.

Revocation and expiration are checked by the app when the share is used. A recipient still needs network access to the StreamLink host while watching a shared stream.

## Playback architecture

- ASP.NET Core Blazor Web App using Interactive Server components.
- `hls.js` handles HLS in browsers without native HLS support.
- `mpegts.js` transmuxes live MPEG-TS for Media Source Extensions (MSE)-capable browsers.
- The server proxy fetches provider manifests and resources, follows provider/CDN redirects, rewrites HLS playlist references, and streams live TS responses.
- FFmpeg optionally converts a continuous TS source to H.264/AAC TS for browser compatibility.
- SQLite (EF Core) stores share-link metadata; ASP.NET Core Data Protection protects session and stream URL data.

HLS and MPEG-TS are containers/delivery formats, not universal codecs. Browser codec support varies by operating system and browser, and transcoding has CPU and bandwidth costs.

## Development

Build the application:

```powershell
dotnet build .\StreamLink\StreamLink.csproj
```

The project targets `net8.0` and uses EF Core SQLite. Browser libraries are loaded from jsDelivr in `StreamLink/Components/App.razor`; a browser needs access to that CDN unless the scripts are self-hosted.

## Troubleshooting

- **Provider sign-in fails:** check the complete server URL and provider credentials; confirm the provider API is reachable from the StreamLink host.
- **No categories/channels:** refresh the page and verify the provider account exposes live-TV categories and streams.
- **Black video, audio-only playback, or unsupported format:** confirm the channel plays in an external player. Install a full FFmpeg build with `libx264` and AAC support, verify the StreamLink process can find it, and restart the app. The browser player reports playback errors beneath the video.
- **Buffering or low quality:** transcoding is real-time CPU work and can add load. Ensure the host can encode faster than playback and the network has enough throughput. If the provider's HLS stream plays well directly, browser playback may avoid server-side transcode costs.
- **Share link unavailable:** links expire after eight hours by default and can be revoked by their owner. Verify the app's SQLite database and Data Protection keys are retained.
- **Time looks wrong:** times are formatted in the browser's configured timezone. Confirm the device timezone and clock.
- **CDN scripts fail to load:** allow jsDelivr or serve the pinned hls.js and mpegts.js assets locally.

## Limitations

- StreamLink is designed for live streams exposed by an Xtream-compatible API; it is not a general playlist/VOD manager.
- It does not provide a transcoding queue, adaptive bitrate ladder, DVR, EPG guide, or guaranteed codec support for every provider stream.
- Transcoding capacity is limited to two concurrent sessions per process and requires adequate CPU/network resources.
- Providers may impose their own account connection limits, geoblocking, device rules, or other restrictions.
- The proxy currently follows upstream redirects to provider/CDN locations to support IPTV edge delivery. Deploy only where outbound network access and trusted provider URLs are appropriately controlled.
