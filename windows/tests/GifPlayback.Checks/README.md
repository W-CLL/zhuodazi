# GIF playback verification

Run on Windows with the .NET 8 desktop runtime:

```powershell
dotnet run --project windows/tests/GifPlayback.Checks/GifPlayback.Checks.csproj --configuration Release
```

This checks every frame in the bundled default GIFs and the historical partial-frame regression fixture against the 3.5.7 compositor, at both thumbnail and pet display sizes. Synthetic frames separately check disposal 2 and 3, restored transparency, loop restarts, noisy images, and a 600-pixel display canvas. Player load, stop, reload and disposal are also covered. Optional additional GIF paths after `--` are read without modification and included in the pixel comparison.

`LegacyGifFrameCompositor.cs` is the frozen 3.5.7 baseline, not production code. Keep it unchanged so new implementations cannot silently redefine the expected rendering.

For independent-process retained-memory and decode-time comparisons:

```powershell
dotnet run --no-build --project windows/tests/GifPlayback.Checks/GifPlayback.Checks.csproj --configuration Release -- --measure old assets/default-pets/024-5826360f.gif 300 3
dotnet run --no-build --project windows/tests/GifPlayback.Checks/GifPlayback.Checks.csproj --configuration Release -- --measure new assets/default-pets/024-5826360f.gif 300 3
```

Arguments are implementation, GIF path, maximum display dimension, and number of players. Each process prints JSON with frame-storage bytes, loading time, process private/working-set bytes, and new playback decoding time. Both measurements settle the GC identically to compare retained objects; production playback does not force a GC or trim the working set. Process figures describe this isolated animation harness, not the entire application. Native/GPU allocations, runtime initialization, and GC heap reservations mean they do not equal pixel-storage bytes.

Recorded on 2026-11-11, Windows x64, three simultaneous animations at maximum 300 pixels:

| GIF | Frames | 3.5.7 private memory | New private memory | Old frame bytes | New stored/display bytes | New decoding per frame |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Default `024-5826360f.gif` | 50 | 63,705,088 | 53,813,248 | 8,640,000 | 1,503,609 | 0.079 ms |
| Imported `190-线条小狗动态表情包.gif` | 13 | 74,522,624 | 55,169,024 | 14,040,000 | 3,111,288 | 0.454 ms |

All 21 bundled/regression GIF files at 48/300 display limits passed 1,518 pixel comparisons. Their aggregate cache plus display-buffer storage decreased from 50,713,344 to 9,353,879 bytes (81.56%). Exact savings and decoding costs depend on frame count, canvas size, content, and hardware. Original image dimensions, composed pixels and frame timing are preserved.
