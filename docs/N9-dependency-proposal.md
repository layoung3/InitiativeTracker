# N9 image processing decision

Pending approval: add `SkiaSharp` and `SkiaSharp.NativeAssets.Linux.NoDependencies`, both pinned to **4.153.1**. No package reference has been added at this checkpoint.

The maintained stable 4.x release provides server-side PNG/JPEG decoding, dimensions before full raster allocation, explicit pixel format, orientation transforms, resizing, and fresh PNG encoding. The Linux native package includes ARM64 builds; publish/package inspection must confirm the actual `linux-arm64` native asset and disposable native execution remains a separate check from real Pi execution.

Both packages use MIT licensing. Retain upstream license and third-party notices with redistributed native binaries. This is a native-code dependency with a larger release archive and ongoing patch obligations. Restrict accepted inputs to PNG/JPEG despite other supported codecs, bound bytes/dimensions/pixels and concurrency, reject animations and truncated decodes, strip metadata by drawing into a new bitmap, and re-encode. Native decoder limits remain a residual attack surface.

Sources checked 2026-10-31: [SkiaSharp package](https://www.nuget.org/packages/SkiaSharp/4.153.1), [Linux native package](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.153.1), [upstream license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md).

Alternative: ImageSharp 4.1.1 is managed and portable, but has Six Labors Split License terms and requires a build-time license key for direct references. Future customer licensing and key provisioning would need separate decisions. ImageSharp 3.1.12 predates the current key enforcement but still has Split License terms and a shorter remaining security-support window. A client-only resize or MIME/extension validation cannot replace a real server decoder. System.Drawing is unsuitable for supported cross-platform .NET/Linux deployments.

Approval concerns only the cloud source/build dependency. It does not authorize installation, deployment, merge or OS changes.
