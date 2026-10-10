using System.Globalization;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using SCMW.Core.Workspace;

namespace SCMW.Core.Projects;

/// <summary>
/// Result of <see cref="ProjectService.Create"/>. <see cref="Project"/> is the project that was
/// created, or null when the name was invalid or already taken — in which case
/// <see cref="ErrorMessage"/> carries one plain message. <see cref="IconWarning"/> is set only when
/// a chosen icon couldn't be used and the project was still created without one. Kept separate from
/// <see cref="ProjectRecord"/> because both messages are transient UI information, never persisted.
/// </summary>
public sealed record CreateResult(ProjectRecord? Project, string? IconWarning, string? ErrorMessage);

/// <summary>What an <see cref="ProjectService.Update"/> call should do with the existing icon.</summary>
public enum IconUpdateKind
{
    /// <summary>Leave the copied icon exactly as it is (the usual case for a name/description edit).</summary>
    Keep,

    /// <summary>Delete the copied icon and clear the field.</summary>
    Remove,

    /// <summary>Replace the copied icon with <see cref="IconUpdate.SourcePath"/>.</summary>
    Replace,
}

/// <summary>
/// An explicit icon instruction for <see cref="ProjectService.Update"/>, so "leave the icon alone"
/// (<see cref="IconUpdateKind.Keep"/>) and "remove the icon" (<see cref="IconUpdateKind.Remove"/>)
/// are never confused with "no new icon supplied".
/// </summary>
public sealed record IconUpdate(IconUpdateKind Kind, string? SourcePath = null);

/// <summary>
/// Result of <see cref="ProjectService.Update"/>. <see cref="Error"/> is a plain message when the
/// edit was refused (nothing changed); <see cref="IconWarning"/> is a plain, non-fatal warning when
/// the name/description were applied but an unusable icon was ignored. At most one is set.
/// </summary>
public sealed record UpdateResult(string? Error, string? IconWarning);

/// <summary>
/// Creates, edits and deletes mod projects under <see cref="WorkspacePaths.Projects"/>.
/// Each project owns a folder named <c>&lt;Name&gt; &lt;shortid8&gt;</c> holding a
/// camelCase <c>project.json</c> ({ id, name, description, createdUtc, modifiedUtc, iconFileName }).
/// Names are unique (case- and whitespace-insensitive). Deletion is recoverable: the whole folder
/// is moved under <see cref="WorkspacePaths.Backups"/> instead of being erased.
/// </summary>
public sealed class ProjectService
{
    /// <summary>File name inside each project folder (also the read probe for <see cref="List"/>).</summary>
    public const string ProjectFileName = "project.json";

    /// <summary>Largest icon accepted, in bytes (4 MB).</summary>
    public const long MaxIconBytes = 4L * 1024 * 1024;

    /// <summary>Copied icon base name; the original extension is kept lowercased (e.g. <c>icon.png</c>).</summary>
    public const string IconBaseName = "icon";

    /// <summary>
    /// The one plain message shown when a chosen icon couldn't be used during creation. The project
    /// is still created without an icon — the typed name/description are never lost.
    /// </summary>
    public const string IconWarningMessage =
        "That file couldn't be used as an icon - the project was created without one. " +
        "Choose a PNG or JPG under 4 MB.";

    /// <summary>Shown when editing an existing project with an unusable icon; the existing icon is kept.</summary>
    public const string IconUpdateWarningMessage =
        "That file couldn't be used as an icon - choose a PNG or JPG under 4 MB.";

    /// <summary>Shown when a name is already in use (case- and whitespace-insensitive).</summary>
    public const string DuplicateNameMessage =
        "A project with that name already exists - choose a different name.";

    /// <summary>Shown when a name is blank.</summary>
    public const string InvalidNameMessage = "Enter a name for the project.";

    /// <summary>Shown when the project id no longer resolves to a folder.</summary>
    public const string NotFoundMessage =
        "That project could not be found - it may have been removed.";

    /// <summary>Shown when a project folder couldn't be renamed.</summary>
    public const string RenameFailedMessage =
        "The project couldn't be renamed. Close anything using it and try again.";

    /// <summary>Shown when a project couldn't be updated.</summary>
    public const string UpdateFailedMessage =
        "The project couldn't be updated. Close anything using it and try again.";

    /// <summary>Shown when a project couldn't be deleted.</summary>
    public const string DeleteFailedMessage =
        "The project couldn't be deleted. Close anything using it and try again.";

    /// <summary>Sub-folder of <see cref="WorkspacePaths.Backups"/> that receives deleted project folders.</summary>
    public const string DeletedProjectsFolderName = "deleted-projects";

    private static readonly string[] AllowedIconExtensions = { ".png", ".jpg", ".jpeg" };

    /// <summary>PNG magic bytes (<c>89 50 4E 47 0D 0A 1A 0A</c>): the file must start with these.</summary>
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>JPEG start-of-image marker (<c>FF D8 FF</c>): any JPEG begins with these three bytes.</summary>
    private static readonly byte[] JpegSignature = { 0xFF, 0xD8, 0xFF };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly WorkspacePaths _ws;

    public ProjectService(WorkspacePaths ws) => _ws = ws;

    /// <summary>
    /// Trim and collapse internal whitespace runs so <c>"My  Mod"</c>, <c>" My Mod "</c> and
    /// <c>"my mod"</c> all compare equal. Returns an empty string when <paramref name="name"/> is blank.
    /// This is the key used for duplicate detection only — the display name is stored as the user
    /// typed it, trimmed (see <see cref="Create"/>).
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var parts = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Create a project folder + <c>project.json</c>. A blank or already-taken name (compared
    /// case- and whitespace-insensitively) is refused with <see cref="CreateResult.ErrorMessage"/>
    /// and nothing is written to disk. Illegal file-name characters are replaced in the folder name
    /// only; the display name is stored trimmed, with the user's inner spacing intact.
    /// When <paramref name="iconSourcePath"/> is given, the image is validated first and then
    /// <em>copied</em> into the project folder (so it survives the original being moved or
    /// deleted). An unusable icon is never fatal: the project is still created, without an icon,
    /// and the returned <see cref="CreateResult.IconWarning"/> carries one plain message.
    /// </summary>
    public CreateResult Create(string name, string description = "", string? iconSourcePath = null)
    {
        var displayName = (name ?? string.Empty).Trim();
        var key = NormalizeName(displayName);
        if (key.Length == 0)
            return new CreateResult(null, null, InvalidNameMessage);
        if (IsNameTaken(key, exceptId: null))
            return new CreateResult(null, null, DuplicateNameMessage);

        var folderName = SanitizeFolderName(displayName);

        // Decide the icon up front. An unusable icon downgrades to "no icon" + a warning; it
        // never blocks creation, so the user's typed name/description are never lost.
        var iconFileName = ResolveIconFileName(iconSourcePath, out var iconWarning);

        string id;
        string folder;
        do
        {
            id = Guid.NewGuid().ToString("N")[..8];
            folder = Path.Combine(_ws.Projects, $"{folderName} {id}");
        }
        while (Directory.Exists(folder)); // random id makes collision vanishingly rare; never overwrite

        Directory.CreateDirectory(folder);

        try
        {
            if (iconFileName is not null && iconSourcePath is not null
                && !TryCopyIconAtomic(iconSourcePath, Path.Combine(folder, iconFileName)))
            {
                iconFileName = null;
                iconWarning = IconWarningMessage;
            }

            var now = DateTime.UtcNow;
            var file = new ProjectFile(id, displayName, description ?? string.Empty, now, now, iconFileName);
            File.WriteAllText(Path.Combine(folder, ProjectFileName),
                JsonSerializer.Serialize(file, JsonOptions));

            var record = new ProjectRecord(id, displayName, file.Description, folder, now, now, iconFileName);
            return new CreateResult(record, iconWarning, null);
        }
        catch
        {
            // A failed write must not leave a half-made project behind.
            TryDeleteDirectory(folder);
            throw;
        }
    }

    /// <summary>
    /// Rename an existing project. The new name is validated exactly like <see cref="Create"/>
    /// (blank/duplicate → plain message, nothing changes); on success the project folder is renamed
    /// (keeping the <c>id8</c> suffix) and <c>project.json</c> is updated. If the folder move fails,
    /// everything is left as it was. Returns null on success.
    /// </summary>
    public string? Rename(string id, string newName)
    {
        var project = GetById(id);
        if (project is null)
            return NotFoundMessage;

        var displayName = (newName ?? string.Empty).Trim();
        var key = NormalizeName(displayName);
        if (key.Length == 0)
            return InvalidNameMessage;
        // Only re-check uniqueness when the normalised name actually changes. A case/whitespace-only
        // self-rename (or a name that normalises to the same value) must not collide with a
        // grandfathered capitalisation twin already in the workspace.
        if (NameActuallyChanged(project.Name, displayName) && IsNameTaken(key, id))
            return DuplicateNameMessage;

        return ApplyEdit(project, displayName, project.Description, project.IconFileName,
            iconSourcePath: null, failureMessage: RenameFailedMessage);
    }

    /// <summary>Replace a project's description, leaving the name and icon alone. Returns null on success.</summary>
    public string? SetDescription(string id, string description)
    {
        var project = GetById(id);
        if (project is null)
            return NotFoundMessage;

        return Collapse(Update(id, project.Name, description, new IconUpdate(IconUpdateKind.Keep)));
    }

    /// <summary>
    /// Replace (<paramref name="iconSourcePath"/> given) or remove (<c>null</c>) a project's icon.
    /// Thin wrapper over <see cref="Update"/> with <see cref="IconUpdateKind.Replace"/> /
    /// <see cref="IconUpdateKind.Remove"/>. Returns null on success, otherwise a plain message
    /// (an unusable image is a warning, not an error, and the existing icon is kept).
    /// </summary>
    public string? SetIcon(string id, string? iconSourcePath)
    {
        if (GetById(id) is null)
            return NotFoundMessage;

        var instruction = string.IsNullOrWhiteSpace(iconSourcePath)
            ? new IconUpdate(IconUpdateKind.Remove)
            : new IconUpdate(IconUpdateKind.Replace, iconSourcePath);
        return Collapse(UpdateForWrapper(id, instruction));
    }

    /// <summary>
    /// Remove a project's icon. Thin wrapper over <see cref="Update"/> with
    /// <see cref="IconUpdateKind.Remove"/>. Returns null on success.
    /// </summary>
    public string? RemoveIcon(string id)
    {
        if (GetById(id) is null)
            return NotFoundMessage;

        return Collapse(UpdateForWrapper(id, new IconUpdate(IconUpdateKind.Remove)));
    }

    /// <summary>
    /// Apply name, description and an explicit icon instruction in one call (used by the inline
    /// editor). The name is validated first; a blank/duplicate name refuses the whole edit with
    /// nothing changed. An <em>unusable</em> replacement image is never fatal: the name and
    /// description are still applied, the existing icon is kept, and <see cref="UpdateResult.IconWarning"/>
    /// is set. A low-level failure (locked folder, failed copy) refuses the whole edit with
    /// <see cref="UpdateResult.Error"/> so no partial state is left behind.
    /// </summary>
    public UpdateResult Update(string id, string name, string description, IconUpdate icon)
    {
        var project = GetById(id);
        if (project is null)
            return new UpdateResult(NotFoundMessage, null);

        var displayName = (name ?? string.Empty).Trim();
        var key = NormalizeName(displayName);
        if (key.Length == 0)
            return new UpdateResult(InvalidNameMessage, null);
        // Re-check uniqueness only when the normalised name changes, so a description-only edit,
        // an icon change or a case-only self-rename of a project that has a capitalisation twin
        // still succeeds. Adopting another project's name is still refused.
        if (NameActuallyChanged(project.Name, displayName) && IsNameTaken(key, id))
            return new UpdateResult(DuplicateNameMessage, null);

        string? desiredIcon = project.IconFileName;
        string? iconSource = null;
        string? iconWarning = null;

        switch (icon.Kind)
        {
            case IconUpdateKind.Keep:
                break;

            case IconUpdateKind.Remove:
                desiredIcon = null;
                break;

            case IconUpdateKind.Replace:
                if (string.IsNullOrWhiteSpace(icon.SourcePath))
                {
                    // Nothing usable to replace with: keep the existing icon, warn, keep the edit.
                    iconWarning = IconUpdateWarningMessage;
                    break;
                }

                var resolved = ResolveIconFileName(icon.SourcePath, out var rejected);
                if (rejected is not null)
                {
                    iconWarning = IconUpdateWarningMessage;
                    break;
                }

                desiredIcon = resolved;
                iconSource = icon.SourcePath;
                break;
        }

        var error = ApplyEdit(project, displayName, description ?? string.Empty, desiredIcon,
            iconSource, UpdateFailedMessage);

        return error is null
            ? new UpdateResult(null, iconWarning)
            : new UpdateResult(error, null);
    }

    /// <summary>Wrapper variant of <see cref="Update"/> that preserves the current name/description.</summary>
    private UpdateResult UpdateForWrapper(string id, IconUpdate icon)
    {
        var project = GetById(id);
        if (project is null)
            return new UpdateResult(NotFoundMessage, null);

        return Update(id, project.Name, project.Description, icon);
    }

    /// <summary>Turn an <see cref="UpdateResult"/> into the single plain message the string-based wrappers return.</summary>
    private static string? Collapse(UpdateResult result) => result.Error ?? result.IconWarning;

    /// <summary>
    /// Recoverable delete: move the whole project folder to
    /// <c>&lt;Workspace&gt;\backups\deleted-projects\&lt;folderName&gt; (yyyyMMdd-HHmmss)</c>.
    /// A name collision gets a numeric counter. Returns null on success; on failure the project is
    /// left intact and a plain message is returned.
    /// </summary>
    public string? Delete(string id)
    {
        var project = GetById(id);
        if (project is null)
            return NotFoundMessage;

        var source = project.FolderPath;
        if (!Directory.Exists(source))
            return DeleteFailedMessage;

        try
        {
            var root = Path.Combine(_ws.Backups, DeletedProjectsFolderName);
            Directory.CreateDirectory(root);

            var folderName = Path.GetFileName(source);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var destination = Path.Combine(root, $"{folderName} ({stamp})");

            var counter = 1;
            while (Directory.Exists(destination) || File.Exists(destination))
            {
                destination = Path.Combine(root, $"{folderName} ({stamp}) ({counter})");
                counter++;
            }

            Directory.Move(source, destination);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            return DeleteFailedMessage;
        }
    }

    /// <summary>
    /// Apply validated changes to an existing project: rename the folder only when the display name
    /// actually changed, then rewrite <c>project.json</c>, and only then apply the icon change (swap
    /// or delete) last. Writing the JSON before touching the icon means a failed write leaves the
    /// icon exactly as it was. If the icon step fails, the just-written JSON is restored so a genuine
    /// icon-copy I/O failure is a whole-edit error (nothing saved). Callers must have validated the
    /// name and resolved the icon already. Returns null on success, <paramref name="failureMessage"/>
    /// otherwise, rolling the folder move back where possible.
    /// </summary>
    private string? ApplyEdit(ProjectRecord project, string newName, string newDescription,
        string? desiredIconFileName, string? iconSourcePath, string failureMessage)
    {
        var currentFolder = project.FolderPath;
        var nameChanged = !string.Equals(project.Name, newName, StringComparison.Ordinal);
        var newFolder = Path.Combine(_ws.Projects, $"{SanitizeFolderName(newName)} {project.Id}");
        var folderChanged = nameChanged && !string.Equals(currentFolder, newFolder, StringComparison.Ordinal);

        if (folderChanged && !TryMoveFolder(currentFolder, newFolder))
            return failureMessage;

        var targetFolder = folderChanged ? newFolder : currentFolder;
        var jsonPath = Path.Combine(targetFolder, ProjectFileName);

        // Write project.json FIRST, before the icon is touched: a failed write therefore leaves the
        // existing icon exactly as it was (the icon step below has not run yet). Keep the old text so
        // the icon step can restore it if it fails.
        string? previousJson;
        try
        {
            previousJson = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            if (folderChanged)
                TryMoveFolder(targetFolder, currentFolder);
            return failureMessage;
        }

        var now = DateTime.UtcNow;
        var file = new ProjectFile(project.Id, newName, newDescription ?? string.Empty,
            project.CreatedUtc, now, desiredIconFileName);
        try
        {
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            if (folderChanged)
                TryMoveFolder(targetFolder, currentFolder);
            return failureMessage;
        }

        // The icon change is deliberately the LAST step. A failed swap/delete restores the JSON we
        // just wrote, so the whole edit is refused and no partial state is left behind.
        if (!TryApplyIcon(targetFolder, desiredIconFileName, iconSourcePath, project.IconFileName))
        {
            TryRestoreJson(jsonPath, previousJson);
            if (folderChanged)
                TryMoveFolder(targetFolder, currentFolder);
            return failureMessage;
        }

        return null;
    }

    /// <summary>
    /// Best-effort restore of <c>project.json</c> after a failed icon step. Failure here is swallowed
    /// (the caller already returns a plain error); the icon itself was never swapped, so the
    /// dangerous half of the edit cannot happen.
    /// </summary>
    private static void TryRestoreJson(string path, string? previousJson)
    {
        try
        {
            if (previousJson is null)
                TryDeleteFile(path);
            else
                File.WriteAllText(path, previousJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Bring the copied icon file in line with the desired state. A new source is copied in as one
    /// atomic replace (copy to a hidden temp file, then move it over the old one) so the old image
    /// survives any failure. When the source already IS the destination the copy is skipped. The old
    /// copy is deleted when the desired name differs or is absent. A null source with a null target
    /// means "no icon". Returns false when the copy/delete fails.
    /// </summary>
    private static bool TryApplyIcon(string folder, string? desiredIconFileName,
        string? iconSourcePath, string? currentIconFileName)
    {
        try
        {
            if (desiredIconFileName is not null && iconSourcePath is not null)
            {
                var destination = Path.Combine(folder, desiredIconFileName);
                if (!SamePath(iconSourcePath, destination)
                    && !TryCopyIconAtomic(iconSourcePath, destination))
                {
                    return false;
                }
            }

            if (currentIconFileName is not null
                && !string.Equals(currentIconFileName, desiredIconFileName, StringComparison.OrdinalIgnoreCase))
            {
                var old = Path.Combine(folder, currentIconFileName);
                if (File.Exists(old))
                    File.Delete(old);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copy <paramref name="source"/> over <paramref name="destination"/> atomically: the bytes go
    /// to a hidden temp file first and only then replace the destination, so a failed copy leaves any
    /// existing destination untouched. Returns false on any failure.
    /// </summary>
    private static bool TryCopyIconAtomic(string source, string destination)
    {
        var folder = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(folder))
            return false;

        var temp = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temp, overwrite: false);
            File.Move(temp, destination, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            TryDeleteFile(temp);
            return false;
        }
    }

    /// <summary>True when two paths point at the same file (case-insensitive, full-path compare).</summary>
    private static bool SamePath(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                       or SecurityException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Move a project folder, handling the Windows case-only rename via a temporary name.</summary>
    private static bool TryMoveFolder(string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
            return true;

        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
        {
            // Windows treats a case-only change as the same path; go through a temporary name.
            var temp = from + ".__renaming__";
            try
            {
                Directory.Move(from, temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or SecurityException or ArgumentException or NotSupportedException)
            {
                return false;
            }

            try
            {
                Directory.Move(temp, to);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or SecurityException or ArgumentException or NotSupportedException)
            {
                try
                {
                    Directory.Move(temp, from);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException
                                                    or SecurityException or ArgumentException or NotSupportedException)
                {
                    // Best effort only.
                }
                return false;
            }
        }

        try
        {
            Directory.Move(from, to);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or SecurityException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="newName"/> differs from the project's current name once both are
    /// normalised (case/whitespace-insensitive). Uniqueness is re-checked only when this is true, so
    /// workspace folders that already hold capitalisation-variant twins remain fully editable.
    /// </summary>
    private static bool NameActuallyChanged(string currentName, string newName) =>
        !string.Equals(NormalizeName(currentName), NormalizeName(newName), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when any other project's name normalizes equal (case-insensitively) to <paramref name="normalizedName"/>.</summary>
    private bool IsNameTaken(string normalizedName, string? exceptId)    {
        foreach (var project in List())
        {
            if (exceptId is not null && string.Equals(project.Id, exceptId, StringComparison.Ordinal))
                continue;
            if (string.Equals(NormalizeName(project.Name), normalizedName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Validate the chosen icon and return the name it will be copied under (e.g. <c>icon.png</c>),
    /// or null when no icon was chosen or the file can't be used. <paramref name="warning"/> is set
    /// to <see cref="IconWarningMessage"/> whenever a chosen icon is rejected.
    /// </summary>
    private static string? ResolveIconFileName(string? iconSourcePath, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(iconSourcePath))
            return null;

        var extension = Path.GetExtension(iconSourcePath).ToLowerInvariant();
        if (Array.IndexOf(AllowedIconExtensions, extension) < 0 || !File.Exists(iconSourcePath))
        {
            warning = IconWarningMessage;
            return null;
        }

        long size;
        try
        {
            size = new FileInfo(iconSourcePath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Locked / unreadable / forbidden files are never fatal: the project is still created.
            warning = IconWarningMessage;
            return null;
        }

        if (size > MaxIconBytes)
        {
            warning = IconWarningMessage;
            return null;
        }

        // The extension only says what the user named it; the contents decide what it really is.
        // A non-image renamed to .png/.jpg is rejected here (a real decoder once accepted one).
        if (!HasValidImageSignature(iconSourcePath, extension))
        {
            warning = IconWarningMessage;
            return null;
        }

        return IconBaseName + extension;
    }

    /// <summary>
    /// Cheap, dependency-free image check: true only when the file's first bytes match the
    /// signature implied by <paramref name="extension"/> (PNG magic for <c>.png</c>, JPEG SOI for
    /// <c>.jpg</c>/<c>.jpeg</c>). Any access failure counts as "not an image" — never throws.
    /// </summary>
    private static bool HasValidImageSignature(string path, string extension)
    {
        var expected = extension == ".png" ? PngSignature : JpegSignature;
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[expected.Length];
            var read = 0;
            while (read < head.Length)
            {
                var n = stream.Read(head, read, head.Length - read);
                if (n == 0)
                    return false; // shorter than the signature → not a valid image
                read += n;
            }

            for (var i = 0; i < expected.Length; i++)
            {
                if (head[i] != expected[i])
                    return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort only: never mask the original failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort only: never mask the original failure.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }

    /// <summary>All readable projects, newest <see cref="ProjectRecord.ModifiedUtc"/> first.</summary>
    public IReadOnlyList<ProjectRecord> List()
    {
        if (!Directory.Exists(_ws.Projects))
            return Array.Empty<ProjectRecord>();

        var records = new List<ProjectRecord>();
        foreach (var folder in Directory.EnumerateDirectories(_ws.Projects))
        {
            var path = Path.Combine(folder, ProjectFileName);
            if (!File.Exists(path))
                continue;

            try
            {
                var file = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), JsonOptions);
                if (file is null || string.IsNullOrEmpty(file.Id))
                    continue;

                records.Add(new ProjectRecord(file.Id, file.Name ?? string.Empty,
                    file.Description ?? string.Empty, folder, file.CreatedUtc, file.ModifiedUtc,
                    file.IconFileName));
            }
            catch (JsonException)
            {
                // A hand-edited/corrupt project file is skipped, never fatal.
            }
            catch (IOException)
            {
                // A locked/unreadable folder is skipped, never fatal.
            }
        }

        return records
            .OrderByDescending(p => p.ModifiedUtc)
            .ThenByDescending(p => p.CreatedUtc)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The project with <paramref name="id"/>, or null when none matches.</summary>
    public ProjectRecord? GetById(string id) =>
        List().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    /// <summary>Replace characters that are illegal in file names, keeping the display name itself unchanged.</summary>
    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
                chars[i] = '_';
        }

        var safe = new string(chars).Trim().TrimEnd('.', ' ');
        return safe.Length == 0 ? "Project" : safe;
    }

    /// <summary>On-disk shape; the folder path and icon path are implied and never written.</summary>
    private sealed record ProjectFile(string Id, string Name, string Description,
        DateTime CreatedUtc, DateTime ModifiedUtc,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IconFileName);
}
