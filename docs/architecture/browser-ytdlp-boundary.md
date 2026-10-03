# Browser / yt-dlp ownership boundary

The browser/media-monitoring path does **not** invoke `YDLProcess`, locate a yt-dlp executable, or ask the browser extension to orchestrate yt-dlp directly.

This is deliberate. yt-dlp remains a **secondary path only for explicitly supported social and video-social services**. It belongs to the social-analysis policy/application boundary and must keep bounded status, cancellation, timeout, and privacy behavior.

This guard does not remove or redefine ADM's separate explicit Video Downloader UI, which owns `YDLProcess`. It prevents the browser-monitoring layer from silently acquiring universal extractor ownership.
