using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace SCMW.GameModel;

/// <summary>
/// THROWAWAY PROBE (2026-10-17). Prints a cooked asset's imports and exports, so we can see whether
/// a cloned-and-renamed asset still points at its old name anywhere. Writes nothing.
/// </summary>
internal static class AssetInspector
{
    internal static int Run(string uassetPath)
    {
        if (!File.Exists(uassetPath))
        {
            Console.WriteLine($"File not found: {uassetPath}");
            return 3;
        }

        try
        {
            var asset = new UAsset(uassetPath, EngineVersion.VER_UE4_27);
            Console.WriteLine($"opened: {Path.GetFileName(uassetPath)}");
            Console.WriteLine($"imports: {asset.Imports.Count}   exports: {asset.Exports.Count}");

            Console.WriteLine("-- imports --");
            for (var i = 0; i < asset.Imports.Count; i++)
            {
                var imp = asset.Imports[i];
                Console.WriteLine($"  [{i}] {imp.ObjectName}  class={imp.ClassName}  outer={imp.OuterIndex}");
            }

            Console.WriteLine("-- exports --");
            for (var i = 0; i < asset.Exports.Count; i++)
            {
                var exp = asset.Exports[i];
                Console.WriteLine($"  [{i}] {exp.ObjectName}  class={exp.ClassIndex}  outer={exp.OuterIndex}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
