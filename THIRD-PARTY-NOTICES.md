# Third-party notices

SCMW — Lone Wolf ("the app") is built on open-source software. This file lists the third-party
components the app uses, and the licence each is offered under. It is provided to satisfy those
licences' attribution requirements.

Versions reflect the current build (`SCMW.App` 1.11.x, .NET 8, `win-x64`). Components marked
"bundled tool" are shipped alongside the app; the rest are libraries linked into it.

## MIT

- **Avalonia** and its modules — UI toolkit (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Skia`,
  `Avalonia.Win32`, `Avalonia.Themes.Fluent`, `Avalonia.Themes.Simple`, `Avalonia.Controls.ColorPicker`,
  `Avalonia.Remote.Protocol`, `Avalonia.Fonts.Inter`). https://avaloniaui.net
- **CommunityToolkit.Mvvm**, **CommunityToolkit.HighPerformance** — MVVM helpers. https://github.com/CommunityToolkit/dotnet
- **Microsoft.Data.Sqlite** (and `Microsoft.Data.Sqlite.Core`) — SQLite access. https://github.com/dotnet/efcore
- **SkiaSharp** (and `SkiaSharp.NativeAssets.Win32`) — 2D graphics. https://github.com/mono/SkiaSharp
- **HarfBuzzSharp** (and `HarfBuzzSharp.NativeAssets.Win32`) — text shaping. https://github.com/mono/SkiaSharp
- **UAssetAPI** — reading/writing Unreal `.uasset` files. https://github.com/atenfyr/UAssetAPI
- **Newtonsoft.Json** — JSON. https://www.newtonsoft.com/json
- **ZstdSharp.Port** — Zstandard compression. https://github.com/oleg-st/ZstdSharp
- **K4os.Compression.LZ4**, **K4os.Compression.LZ4.Streams**, **K4os.Hash.xxHash** — LZ4 and xxHash. https://github.com/MiloszKrajewski/K4os.Compression.LZ4
- **BouncyCastle.Cryptography** — cryptography. https://www.bouncycastle.org
- **SharpGLTF.Core**, **SharpGLTF.Toolkit** — glTF. https://github.com/vpenades/SharpGLTF
- **AssetRipper.TextureDecoder** — texture decoding. https://github.com/AssetRipper/TextureDecoder
- **Infrablack.UE4Config** — Unreal config parsing. https://github.com/Infrablack/UE4Config
- **MicroCom.Runtime**, **OffiUtils** — native interop helpers (used by Avalonia). https://github.com/kekekeks
- **Oodle.NET** — Oodle bindings. https://github.com/WorkingRobot/Oodle.NET
- **System.IO.Pipelines**, **System.Memory** — .NET runtime helpers. https://github.com/dotnet
- **Tmds.DBus.Protocol** — D-Bus (Avalonia, non-Windows). https://github.com/tmds/Tmds.DBus
- **Zlib-ng.NET** — zlib. https://github.com/zlib-ng
- **LZMA SDK** — LZMA. https://www.7-zip.org/sdk.html
- **Avalonia.Diagnostics** — developer tools (included in debug builds only).

## Apache-2.0

- **Serilog**, **Serilog.Sinks.File**, **Serilog.Sinks.Console** — logging. https://serilog.net
- **CUE4Parse**, **CUE4Parse-Conversion** — reading Unreal Engine `.pak` archives. https://github.com/FabianFG/CUE4Parse
- **SQLitePCLRaw** (`core`, `provider.e_sqlite3`, `bundle_e_sqlite3`, `lib.e_sqlite3`) — SQLite bindings. https://github.com/ericsink/SQLitePCL.raw

## BSD-2-Clause

- **Blake3** — hashing. https://github.com/xoofx/Blake3.NET

## BSD-3-Clause

- **ANGLE** (`Avalonia.Angle.Windows.Natives`) — graphics translation, bundled by Avalonia. https://github.com/google/angle

## SIL Open Font License 1.1

- **Inter** typeface — the app's UI font, bundled via `Avalonia.Fonts.Inter`. https://rsms.me/inter

## Public domain

- **SQLite** — the embedded database engine (via `SQLitePCLRaw.lib.e_sqlite3`). https://sqlite.org

## Dual-licensed (MIT OR Apache-2.0)

- **repak** — bundled tool `tools/repak/repak.exe` for packing `.pak` files. Licence texts ship with it
  as `tools/repak/LICENSE-MIT` and `tools/repak/LICENSE-APACHE`. https://github.com/trumank/repak

---

Full licence texts for each component are available at the project links above. The standard
licence texts that cover these components also ship with the app in its **`licenses/`** folder
(`MIT.txt`, `Apache-2.0.txt`, `BSD-2-Clause.txt`, `BSD-3-Clause.txt`, `OFL-1.1.txt`, `ANGLE.txt`),
alongside `repak`'s own licence files under `tools/repak/`.
