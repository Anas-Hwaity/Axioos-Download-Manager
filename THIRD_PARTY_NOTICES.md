# Third-party notices

This file is the public entry point for third-party attribution. The detailed release audit is maintained in `docs/release/third-party-notices.md`.

## Upstream XDM

This project contains and modifies Xtreme Download Manager source distributed under GPLv2 terms. Preserve the repository `LICENSE`, upstream attribution, source-availability obligations, and modification notices in redistributed builds.

## External analyzers and media tools

`yt-dlp`, ffmpeg, and any other external executable are separate dependencies. A release may include a binary only when its exact artifact, version, provenance, SHA-256, and license status are recorded and approved. The authoritative machine-readable inventory is `dependencies/external-binaries.json`.

The current repository intentionally treats unresolved external-binary pins as a release blocker rather than guessing provenance or license status.

The yt-dlp source is under the Unlicense, while its standalone PyInstaller executables include components with other licenses, including GPLv3+ code. Audit the exact executable distributed with Axioos and include its applicable third-party notices. See the [yt-dlp licensing notes](https://github.com/yt-dlp/yt-dlp/blob/master/README.md#licensing).

On Windows, Axioos can download Deno for YouTube analysis. Deno is distributed under the MIT license; see the [Deno license](https://github.com/denoland/deno/blob/main/LICENSE.md). Record the exact downloaded release artifact before publication.

## Bundled interface fonts

Sora, Hanken Grotesk, Space Grotesk, Outfit, Manrope, and JetBrains Mono are bundled under the SIL Open Font License 1.1. The corresponding license texts are included in `ADM/ADM.Wpf.UI/WebShell/fonts/`.

## Microsoft Edge WebView2

The Axioos main window is an HTML page hosted in the Microsoft Edge WebView2 control through the `Microsoft.Web.WebView2` NuGet package, distributed by Microsoft under the BSD-style license shipped inside that package. The WebView2 Runtime itself is part of Windows 10 and 11 and is not redistributed. When the runtime is missing, Axioos keeps the classic window.
