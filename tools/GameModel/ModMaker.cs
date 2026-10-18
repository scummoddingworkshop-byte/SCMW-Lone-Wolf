using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace SCMW.GameModel;

/// <summary>
/// Make mode: a throwaway aid that writes small change-test mods. For each <c>--set</c> it reads the
/// named game package out of the mounted paks, applies the field edits on a copy, lays the changed
/// files out on the game's own tree, and packs them into one <c>_P.pak</c> patch mod. It NEVER
/// installs anything — where the pak goes is the caller's business.
///
/// <para>A plain text field (an item's <c>Caption</c>, a base element's <c>_name</c>) is an FText
/// whose <b>words</b> live in its source string, not the key the app's writer sets. So those edits
/// are written here directly, so the test can change the words while keeping the key.</para>
/// </summary>
internal static class ModMaker
{
    /// <summary>Applies <paramref name="edits"/>, packs the result, and returns the tool's exit code.</summary>
    internal static int Run(string outPak, IReadOnlyList<SetEdit> edits, PakMount mount, string repakPath)
    {
        var reader = new PakMountPackageReader(mount);
        var writer = new UAssetPackageWriter();
        var packer = new RepakPacker(repakPath, new SystemProcessRunner());

        var root = Path.Combine(Path.GetTempPath(), "scmw-gamemodel-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "work");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(staging);

        try
        {
            foreach (var group in edits.GroupBy(e => e.AssetId, StringComparer.OrdinalIgnoreCase))
            {
                if (!reader.TryReadPackage(group.Key, out var files) || files is null || files.Count == 0)
                {
                    Console.Error.WriteLine($"The game file for '{group.Key}' could not be read.");
                    return 3;
                }

                var packageDir = Path.Combine(work, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(packageDir);
                string? uassetPath = null;
                foreach (var pair in files)
                {
                    var destination = Path.Combine(packageDir, pair.Key);
                    File.WriteAllBytes(destination, pair.Value);
                    if (destination.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) uassetPath = destination;
                }

                if (uassetPath is null)
                {
                    Console.Error.WriteLine($"'{group.Key}' had no .uasset file to change.");
                    return 3;
                }

                var assetName = Path.GetFileNameWithoutExtension(uassetPath);

                // A text field's words are its source string, and are set apart from the app's writer.
                var textEdits = group
                    .Where(e => FindText(uassetPath, assetName, e.Field) is not null)
                    .Select(e => (e.Field, e.Value))
                    .ToList();
                var otherChanges = group
                    .Where(e => FindText(uassetPath, assetName, e.Field) is null)
                    .Select(e => new FieldChange(e.Field, e.Value))
                    .ToList();

                if (otherChanges.Count > 0)
                {
                    var written = writer.Apply(uassetPath, otherChanges);
                    if (!written.Ok)
                    {
                        Console.Error.WriteLine(written.Error);
                        return 3;
                    }
                }

                if (textEdits.Count > 0 && !ApplyTextEdits(uassetPath, assetName, textEdits, out var textError))
                {
                    Console.Error.WriteLine(textError);
                    return 3;
                }

                // The pak keeps the game's own path for each file, so the patch overrides the original.
                var targetDir = Path.Combine(staging, group.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(targetDir);
                foreach (var file in Directory.GetFiles(packageDir))
                    File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);

                foreach (var edit in group)
                    Console.WriteLine($"  {group.Key}: {edit.Field} = {edit.Value}");
            }

            var packed = packer.PackAsync(staging, outPak, CancellationToken.None).GetAwaiter().GetResult();
            if (!packed.Ok)
            {
                Console.Error.WriteLine(packed.Error);
                return 3;
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }

        Console.WriteLine($"Wrote test mod: {outPak}");
        return 0;
    }

    /// <summary>The top-level text property named <paramref name="field"/> on any export, or null.</summary>
    private static TextPropertyData? FindText(string uassetPath, string assetName, string field)
    {
        try
        {
            var asset = new UAsset(uassetPath, EngineVersion.VER_UE4_27);
            var cdo = $"Default__{assetName}_C";
            foreach (var export in asset.Exports.OfType<NormalExport>()
                         .OrderByDescending(e => e.ObjectName.ToString() == cdo))
                foreach (var property in export.Data)
                    if (property is TextPropertyData text
                        && string.Equals(property.Name.ToString(), field, StringComparison.OrdinalIgnoreCase))
                        return text;
        }
        catch
        {
            // Not readable (or not a text field): leave it to the app's writer, which reports plainly.
        }

        return null;
    }

    /// <summary>Sets each text field's source words, keeping its key, then saves the package.</summary>
    private static bool ApplyTextEdits(
        string uassetPath, string assetName, IReadOnlyList<(string Field, string Value)> edits, out string? error)
    {
        error = null;
        try
        {
            var asset = new UAsset(uassetPath, EngineVersion.VER_UE4_27);
            var cdo = $"Default__{assetName}_C";
            var exports = asset.Exports.OfType<NormalExport>()
                .OrderByDescending(e => e.ObjectName.ToString() == cdo)
                .ToList();

            foreach (var (field, value) in edits)
            {
                TextPropertyData? found = null;
                foreach (var export in exports)
                    foreach (var property in export.Data)
                        if (property is TextPropertyData text
                            && string.Equals(property.Name.ToString(), field, StringComparison.OrdinalIgnoreCase))
                        {
                            found = text;
                            break;
                        }

                if (found is null)
                {
                    error = $"The text field \"{field}\" was not found in \"{assetName}\".";
                    return false;
                }

                found.CultureInvariantString = FString.FromString(value);
            }

            asset.Write(uassetPath);
            return true;
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"GameModel: setting text failed ({ex.GetType().Name}).");
            error = $"The text in \"{assetName}\" could not be changed.";
            return false;
        }
    }
}
