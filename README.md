# StreamLink

Turn IPTV channels into easy-to-use stream and share links.

## Live playback

The browser tries server-side FFmpeg playback first, converting live MPEG-TS to H.264/AAC. The encoder preserves source resolution and frame rate and uses quality-targeted CRF 18 (capped at 10 Mbps) rather than forcing low-bitrate 1080p. If FFmpeg fails, it automatically tries provider HLS and then original TS. FFmpeg is required on the server (`ffmpeg` on PATH, or set `FFmpeg:Path` to the executable). Restart the app after installing FFmpeg. Transcoding uses CPU and is limited to two simultaneous viewers. Higher quality can still buffer if the provider, server CPU, or viewer connection cannot sustain the stream; transcoding cannot restore detail absent from the source, fix DRM, or repair offline streams.
