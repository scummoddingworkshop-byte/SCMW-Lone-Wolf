// src/SCMW.Backend.Pak/IPakMount.cs
// Testability seam between PakContentSource and the real CUE4Parse provider
// wrapper (PakMount). Kept minimal: enumerate the mounted pak's files and load
// a package object. Everything CUE4Parse-specific is hidden behind this so the
// unit tests can substitute a fake mount without touching real game files.
using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Exports;

namespace SCMW.Backend.Pak;

public interface IPakMount : IDisposable
{
    bool IsMounted { get; }

    int FileCount { get; }

    IEnumerable<string> FilePaths { get; }

    /// <summary>
    /// Creates the underlying provider and submits the AES key (when given).
    /// Must never throw: failures are returned as <paramref name="error"/>.
    /// </summary>
    bool TryMount(string pakPath, string? aesKey, out string? error);

    /// <summary>
    /// Loads a single package object (e.g. ".../Improvised_Bow.Default__Improvised_Bow_C").
    /// Returns false (with a null object) rather than throwing.
    /// </summary>
    bool TryLoadPackageObject(string path, out UObject? obj);

    /// <summary>
    /// Loads every export object in a package — the item itself plus its component objects
    /// (where fields such as MaxQuantity live). Default returns false so simple fakes need not
    /// implement it; the real <see cref="PakMount"/> overrides it.
    /// </summary>
    bool TryLoadExports(string packagePath, out IReadOnlyList<UObject> exports)
    {
        exports = Array.Empty<UObject>();
        return false;
    }

    /// <summary>
    /// Reads a package's raw files (<c>.uasset</c>/<c>.uexp</c>/…) keyed by file name — the same
    /// bytes the build writer edits. Used by the nested-value reader, which needs the writer's
    /// library (UAssetAPI) to see inside structs. Default returns false so simple fakes need not
    /// implement it; the real <see cref="PakMount"/> overrides it.
    /// </summary>
    bool TryReadPackage(string packagePath, out IReadOnlyDictionary<string, byte[]>? files)
    {
        files = null;
        return false;
    }
}
