namespace SCMW.Backend.Build;

using SCMW.Core.Editing;

/// <summary>
/// The build pipeline: resolve each item, read it from the current paks, write its values on a
/// copy, lay the copy out on the game's own tree, then pack. Any failure stops the whole build —
/// no `.pak`, nothing installed. Reports phases 0..4 for the step bar.
/// </summary>
public sealed class ModBuilder : IModBuilder
{
    private readonly IPackageReader _reader;
    private readonly IPackageWriter _writer;
    private readonly IPacker _packer;
    private readonly IAssetResolver _resolver;
    private readonly string _buildsRoot;

    public ModBuilder(IPackageReader reader, IPackageWriter writer, IPacker packer,
                      IAssetResolver resolver, string buildsRoot)
    {
        _reader = reader;
        _writer = writer;
        _packer = packer;
        _resolver = resolver;
        _buildsRoot = buildsRoot;
    }

    public async Task<BuildOutcome> BuildAsync(BuildRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        progress?.Report(0);
        if (request.Items.Count == 0)
            return new BuildOutcome(false, null, "There is nothing to build — change an item value first.");

        var buildFolder = BuildNames.BuildFolder(_buildsRoot, request.ProjectName);
        var staging = BuildNames.StagingFor(buildFolder);
        var work = Path.Combine(buildFolder, "work");

        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(work);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BuildOutcome(false, null,
                "The build folder could not be prepared. Check that the app can write to its workspace.");
        }

        // Everything below is wrapped so ANY unexpected error becomes one plain failure, never an
        // escaping exception that would leave the screen spinning.
        try
        {
            progress?.Report(1);
            progress?.Report(2);

            // Resolve every item first. Two items can land on the same game file (one recorded by
            // name, one by path), so their edits are grouped and applied together — otherwise the
            // second copy would silently overwrite the first.
            var packages = new Dictionary<string, (string Name, List<FieldChange> Changes)>(
                StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var item in request.Items)
            {
                var resolved = await _resolver.ResolveAsync(item.RecordedName, ct).ConfigureAwait(false);
                if (resolved.PackagePath is null)
                    return new BuildOutcome(false, null, resolved.Error ?? $"\"{item.RecordedName}\" could not be found.");

                if (!packages.TryGetValue(resolved.PackagePath, out var group))
                {
                    group = (item.RecordedName, new List<FieldChange>());
                    packages[resolved.PackagePath] = group;
                    order.Add(resolved.PackagePath);
                }
                group.Changes.AddRange(item.Changes);
            }

            foreach (var packagePath in order)
            {
                var (name, changes) = packages[packagePath];

                if (!_reader.TryReadPackage(packagePath, out var files) || files is null || files.Count == 0)
                    return new BuildOutcome(false, null, $"The game files for \"{name}\" could not be read.");

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
                    return new BuildOutcome(false, null, $"\"{name}\" had no .uasset file to change.");

                var donors = await ExtractDonorsAsync(changes, work, ct).ConfigureAwait(false);

                var written = _writer.Apply(uassetPath, changes, donors);
                if (!written.Ok)
                    return new BuildOutcome(false, null, written.Error ?? $"\"{name}\" could not be changed.");

                var relative = packagePath.Replace('/', Path.DirectorySeparatorChar);
                var targetFile = Path.Combine(staging, relative);
                var targetDir = Path.GetDirectoryName(targetFile)!;
                Directory.CreateDirectory(targetDir);
                foreach (var file in Directory.GetFiles(packageDir))
                    File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
            }

            progress?.Report(3);
            var pakPath = Path.Combine(buildFolder, BuildNames.PakFileName(request.ProjectName, request.Version));
            if (File.Exists(pakPath)) File.Delete(pakPath);

            var packed = await _packer.PackAsync(staging, pakPath, ct).ConfigureAwait(false);
            if (!packed.Ok)
                return new BuildOutcome(false, null, packed.Error);

            try
            {
                File.WriteAllText(Path.Combine(buildFolder, "build.json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        project = request.ProjectName,
                        version = BuildNames.NormalizeVersion(request.Version),
                        items = request.Items.Count,
                        builtUtc = DateTime.UtcNow,
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A missing written note never fails a good pak; it is only a record.
            }

            progress?.Report(4);
            return new BuildOutcome(true, pakPath, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Leave a trail: the plain message is for the user, the detail is for the log.
            SCMW.Core.Logging.Log.Warn($"Build failed: {ex.GetType().Name}: {ex.Message}");
            return new BuildOutcome(false, null,
                "The mod could not be made. Something went wrong while reading or writing the game files.");
        }
    }

    /// <summary>
    /// Extracts, into the work folder, the donor item's `.uasset` for each "add a field" change —
    /// keyed by the field's own name — so the writer can copy the field's exact shape. Returns null
    /// when no change adds a field. A donor that cannot be found or read is simply left out; the
    /// writer then fails plainly rather than guessing a shape.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> ExtractDonorsAsync(
        IReadOnlyList<FieldChange> changes, string work, CancellationToken ct)
    {
        var addChanges = changes.Where(c => c.Operation == ChangeOperation.AddField).ToList();
        if (addChanges.Count == 0) return null;

        var donors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in addChanges)
        {
            if (donors.ContainsKey(change.Field) || string.IsNullOrEmpty(change.DonorPath))
                continue;

            var resolved = await _resolver.ResolveAsync(change.DonorPath, ct).ConfigureAwait(false);
            if (resolved.PackagePath is null) continue;

            if (!_reader.TryReadPackage(resolved.PackagePath, out var files) || files is null || files.Count == 0)
                continue;

            var donorDir = Path.Combine(work, "donor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(donorDir);

            string? donorUasset = null;
            foreach (var pair in files)
            {
                var destination = Path.Combine(donorDir, pair.Key);
                File.WriteAllBytes(destination, pair.Value);
                if (destination.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) donorUasset = destination;
            }

            if (donorUasset is not null) donors[change.Field] = donorUasset;
        }

        return donors.Count > 0 ? donors : null;
    }
}
