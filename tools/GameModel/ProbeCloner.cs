using SCMW.Backend.Build;
using SCMW.Backend.Pak;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace SCMW.GameModel;

/// <summary>
/// THROWAWAY PROBE (2026-10-17). Answers one feasibility question before we build "create new
/// content" for real: can a pak carry a BRAND-NEW game asset — a copy of an existing one, renamed
/// into a new file at a new path — that SCUM still loads without falling over? It is not a keeper;
/// the real clone-and-rename capability is built in the backend once the answer is known. See
/// docs/Handover - Create new content.md.
/// </summary>
internal static class ProbeCloner
{
    /// <summary>Reads <paramref name="fromAssetId"/> from the game, renames its object to
    /// <paramref name="asAssetId"/>'s last segment, lays the copy out at the new path, and packs it.</summary>
    internal static int Run(string outPak, string fromAssetId, string asAssetId, PakMount mount, string repakPath,
                            string? registryPath = null)
    {
        var reader = new PakMountPackageReader(mount);
        var packer = new RepakPacker(repakPath, new SystemProcessRunner());

        var oldName = LastSegment(fromAssetId);
        var newName = LastSegment(asAssetId);

        var root = Path.Combine(Path.GetTempPath(), "scmw-clone-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, "work");
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(staging);

        try
        {
            if (!reader.TryReadPackage(fromAssetId, out var files) || files is null || files.Count == 0)
            {
                Console.Error.WriteLine($"The game file '{fromAssetId}' could not be read.");
                return 3;
            }

            string? uassetPath = null;
            foreach (var pair in files)
            {
                var destination = Path.Combine(work, pair.Key);
                File.WriteAllBytes(destination, pair.Value);
                if (destination.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) uassetPath = destination;
            }

            if (uassetPath is null)
            {
                Console.Error.WriteLine($"'{fromAssetId}' had no .uasset file.");
                return 3;
            }

            if (!TryRename(uassetPath, oldName, newName, out var error))
            {
                Console.Error.WriteLine(error);
                return 3;
            }

            // Lay the clone out at the NEW path, named after the new asset — a file the game does
            // not have. A cooked package sits at "<folder>/<AssetName>.uasset", NOT inside a folder
            // named after the asset. Getting this one level wrong makes the game fail to find it.
            var lastSlash = asAssetId.LastIndexOf('/');
            var targetFolder = lastSlash >= 0 ? asAssetId[..lastSlash] : asAssetId;
            var targetDir = Path.Combine(staging, targetFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(work))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith(oldName, StringComparison.Ordinal))
                    name = newName + name[oldName.Length..];
                File.Copy(file, Path.Combine(targetDir, name), true);
                Console.WriteLine($"  staged {targetFolder}/{name}");
            }

            // A brand-new file is invisible unless the game's own index is told about it, so if a
            // registry was given, add our asset to it and stage the whole thing at SCUM/AssetRegistry.bin.
            if (!string.IsNullOrEmpty(registryPath))
            {
                if (!File.Exists(registryPath))
                {
                    Console.Error.WriteLine($"The registry file was not found: {registryPath}");
                    return 3;
                }

                var registry = File.ReadAllBytes(registryPath);
                var gamePath = asAssetId.Replace("SCUM/Content/", "/Game/", StringComparison.OrdinalIgnoreCase);
                var slash = gamePath.LastIndexOf('/');
                var packagePath = gamePath[..slash];
                var assetName = gamePath[(slash + 1)..];
                var donorName = LastSegment(fromAssetId);

                var augmented = RegistryWriter.TryAddAsset(registry, packagePath, assetName, donorName, out var regError);
                if (augmented is null)
                {
                    Console.Error.WriteLine($"The registry could not be updated: {regError}");
                    return 3;
                }

                var registryTarget = Path.Combine(staging, "SCUM", "AssetRegistry.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(registryTarget)!);
                File.WriteAllBytes(registryTarget, augmented);
                Console.WriteLine($"  staged SCUM/AssetRegistry.bin  ({registry.Length:N0} -> {augmented.Length:N0} bytes)");
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

        Console.WriteLine($"Wrote probe mod: {outPak}");
        return 0;
    }

    /// <summary>Probe: writes a pak holding only a registry with one asset removed (a read test).</summary>
    internal static int RunRegistryDrop(string outPak, string registryPath, string assetName, string repakPath)
    {
        var packer = new RepakPacker(repakPath, new SystemProcessRunner());
        var root = Path.Combine(Path.GetTempPath(), "scmw-regdrop-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(staging);
        try
        {
            if (!File.Exists(registryPath))
            {
                Console.Error.WriteLine($"The registry file was not found: {registryPath}");
                return 3;
            }

            var registry = File.ReadAllBytes(registryPath);
            var dropped = RegistryWriter.TryRemoveAsset(registry, assetName, out var error);
            if (dropped is null)
            {
                Console.Error.WriteLine($"The registry could not be updated: {error}");
                return 3;
            }

            var target = Path.Combine(staging, "SCUM", "AssetRegistry.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, dropped);
            Console.WriteLine($"  staged SCUM/AssetRegistry.bin  ({registry.Length:N0} -> {dropped.Length:N0} bytes; dropped \"{assetName}\")");

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

        Console.WriteLine($"Wrote probe mod: {outPak}");
        return 0;
    }

    /// <summary>Renames the asset's own object (and its class-default twin, if present) so the clone
    /// is a distinct object, then saves the package.</summary>
    private static bool TryRename(string uassetPath, string oldName, string newName, out string? error)
    {
        error = null;
        try
        {
            var asset = new UAsset(uassetPath, EngineVersion.VER_UE4_27);
            var cdoOld = $"Default__{oldName}_C";
            var cdoNew = $"Default__{newName}_C";

            var renamed = 0;
            foreach (var export in asset.Exports)
            {
                var current = export.ObjectName.ToString();
                if (string.Equals(current, oldName, StringComparison.Ordinal))
                {
                    export.ObjectName = FName.FromString(asset, newName);
                    renamed++;
                }
                else if (string.Equals(current, cdoOld, StringComparison.Ordinal))
                {
                    export.ObjectName = FName.FromString(asset, cdoNew);
                    renamed++;
                }
            }

            if (renamed == 0)
            {
                error = $"No object named '{oldName}' was found inside '{Path.GetFileName(uassetPath)}' to rename.";
                return false;
            }

            asset.Write(uassetPath);
            Console.WriteLine($"  renamed {renamed} object(s): {oldName} -> {newName}");
            return true;
        }
        catch (Exception ex)
        {
            SCMW.Core.Logging.Log.Warn($"Probe clone: rename failed ({ex.GetType().Name}).");
            error = $"Cloning '{oldName}' failed while renaming: {ex.Message}";
            return false;
        }
    }

    private static string LastSegment(string assetId)
    {
        var slash = assetId.LastIndexOf('/');
        return slash >= 0 ? assetId[(slash + 1)..] : assetId;
    }
}
