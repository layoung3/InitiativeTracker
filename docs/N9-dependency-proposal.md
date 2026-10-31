# N9 image processing decision

Approved for source/build changes: `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`, both pinned to **4.154.0**. The owner explicitly approved both exact versions; this does not authorize merge, deployment or OS changes. Both references are now included.

The latest stable release, published 2026-10-28, provides server-side PNG/JPEG decoding, dimensions before full raster allocation, explicit pixel format, orientation transforms, resizing, and fresh PNG encoding. Read-only inspection of both official 4.154.0 NuGet archives confirmed version/license metadata and .NET 10 assets. The Linux native archive contains `runtimes/linux-arm64/native/libSkiaSharp.so`, identified as an ELF AArch64 shared object (about 12 MiB uncompressed). Its full multiarchitecture NuGet download is about 60 MiB. Managed SkiaSharp also restores matching Win32/macOS runtime packages for desktop targets; RID-specific Linux publishing must exclude irrelevant runtimes. The approved implementation uses these packages. x64 tests execute their native decoder; a separate native ARM64 CI job publishes and executes the same pipeline from its self-contained artifact. Consult the final-head CI evidence rather than inferring execution from ELF inspection.

Both package manifests declare MIT licensing. Retain upstream license and third-party notices with redistributed native binaries. This is a native-code dependency with a larger release archive and ongoing patch obligations. Restrict accepted inputs to PNG/JPEG despite other supported codecs, bound bytes/dimensions/pixels and concurrency, reject animations and truncated decodes, strip metadata by drawing into a new bitmap, and re-encode. Native decoder limits remain a residual attack surface.

Sources checked 2026-10-31: [SkiaSharp package](https://www.nuget.org/packages/SkiaSharp/4.154.0), [Linux native package](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.154.0), [upstream license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md).

Alternative: ImageSharp 4.1.1 is managed and portable, but has Six Labors Split License terms and requires a build-time license key for direct references. Future customer licensing and key provisioning would need separate decisions. ImageSharp 3.1.12 predates the current key enforcement but still has Split License terms and a shorter remaining security-support window. A client-only resize or MIME/extension validation cannot replace a real server decoder. System.Drawing is unsuitable for supported cross-platform .NET/Linux deployments.

Approval concerns only the cloud source/build dependency. It does not authorize installation, deployment, merge or OS changes.

## Currency and advisory check, 2026-10-31

The earlier 4.153.1 recommendation was stale. Upstream marks [4.154.0](https://github.com/mono/SkiaSharp/releases/tag/v4.154.0) as latest stable, with 4.153.2 released the same day for the earlier engine line. 4.155/4.156 remain prereleases. Recommend matching **4.154.0** packages rather than the older patch line. The [release notes](https://mono.github.io/SkiaSharp/docs/releases/4.154.0.html) describe the M154 engine update, upstream corrections and disabled RAW/DNG decoding; N9 still explicitly accepts only PNG/JPEG.

The current official [NuGet vulnerability feed](https://api.nuget.org/v3/vulnerabilities/index.json) was checked directly: its SkiaSharp entry is GHSA-j7hp-h8jx-5ppr, affecting `[2.0.0, 2.88.6)`. No feed entry covered 4.154.0 or its matching Linux native package. [Upstream advisories](https://github.com/mono/SkiaSharp/security/advisories) showed none published. These checks do not establish absence of undisclosed or inherited native-code vulnerabilities. Repeat direct/transitive package audit after restore and before release; continue applying native decoder resource limits.

The downloaded native package contains `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` with additional third-party material (including BSD/Apache notices). Preserve those exact files with redistributed binaries. No commercial license key is required by these MIT packages. The corrected exact versions were explicitly approved by the owner for source/build changes only.

## Native dependency inspection and audit

The actual ARM64 `libSkiaSharp.so` declares `libstdc++.so.6`, `libpthread.so.0`, `libdl.so.2`, `libm.so.6`, `libc.so.6` and `librt.so.1` dependencies. Highest observed GLIBC symbol version is 2.27; highest observed GLIBCXX version is 3.4.22. These are inspection results, not Pi OS execution. Existing fresh hub prerequisites already include libc6/libstdc++6/libgcc-s1. No Fontconfig package is added.

Direct/transitive `dotnet list package --include-transitive --vulnerable --format json` audit returned no vulnerable packages in the three projects. The new managed package restores matching Win32/macOS native packages 4.154.0 during cross-platform development; RID-specific Linux publishing selects Linux assets. CI repeats the audit and audits the pinned Playwright development dependency. Keep the official `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` under the published `ThirdPartyNotices/SkiaSharp/` directory. A clean feed result is point-in-time evidence, not a guarantee against unknown native decoder vulnerabilities.
