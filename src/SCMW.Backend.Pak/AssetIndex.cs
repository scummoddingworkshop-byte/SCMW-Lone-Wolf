// src/SCMW.Backend.Pak/AssetIndex.cs
// Local SQLite index of a pak scan (Task 5): the UI's instant search over
// ~6.8k items without re-mounting the pak. All SQL is parameterized; the
// index file is a disposable cache, safe to delete between builds.
using System.Globalization;
using Microsoft.Data.Sqlite;
using SCMW.Core;
using SCMW.Core.Content;

namespace SCMW.Backend.Pak;

public sealed class AssetIndex : IAssetIndex
{
    // Same page-size cap as PakContentSource.SearchAsync (shared constant).
    private const int MaxTake = ContentLimits.MaxTake;

    private const string BuildIdKey = "game_build_id";
    private const string LastScanKey = "last_scan_utc";

    // Idempotent by construction (IF NOT EXISTS): missing pieces heal on the
    // next call, repeated calls are cheap no-ops.
    private static readonly string[] SchemaDdl =
    {
        @"CREATE TABLE IF NOT EXISTS assets(
             id TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL,
             technical_path TEXT NOT NULL,
             overall INTEGER NOT NULL, editable_count INTEGER NOT NULL, icon_key TEXT NULL)",
        "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL)",
        "CREATE INDEX IF NOT EXISTS ix_assets_name ON assets(name)",
        "CREATE INDEX IF NOT EXISTS ix_assets_category ON assets(category)",
    };

    public void EnsureSchema(string dbPath)
    {
        using var _ = Open(dbPath);
    }

    public void ReplaceAll(string dbPath, string gameBuildId, IEnumerable<AssetSummary> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(gameBuildId);

        using var conn = Open(dbPath);
        using var tx = conn.BeginTransaction();

        // Everything below shares one transaction: a failure anywhere (for
        // example a constraint violation mid-sequence) rolls the whole
        // replace back, leaving the previous contents and build id intact.

        using (var delete = conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM assets";
            delete.ExecuteNonQuery();
        }

        using (var setBuild = conn.CreateCommand())
        {
            setBuild.Transaction = tx;
            setBuild.CommandText = """
                INSERT INTO meta(key, value) VALUES(@key, @value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            setBuild.Parameters.Add("@key", SqliteType.Text).Value = BuildIdKey;
            setBuild.Parameters.Add("@value", SqliteType.Text).Value = gameBuildId;
            setBuild.ExecuteNonQuery();
        }

        // Record when the scan completed so the Settings screen can show
        // "Last scan: {time} …". Same transaction as the rows + build id, so a
        // failed replace rolls this back too (the previous scan time survives).
        using (var setLast = conn.CreateCommand())
        {
            setLast.Transaction = tx;
            setLast.CommandText = """
                INSERT INTO meta(key, value) VALUES(@key, @value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            setLast.Parameters.Add("@key", SqliteType.Text).Value = LastScanKey;
            setLast.Parameters.Add("@value", SqliteType.Text).Value =
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            setLast.ExecuteNonQuery();
        }

        using (var insert = conn.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO assets(id, name, category, technical_path, overall, editable_count, icon_key)
                VALUES(@id, @name, @category, @technical_path, @overall, @editable_count, @icon_key)
                """;
            var id = insert.Parameters.Add("@id", SqliteType.Text);
            var name = insert.Parameters.Add("@name", SqliteType.Text);
            var category = insert.Parameters.Add("@category", SqliteType.Text);
            var technicalPath = insert.Parameters.Add("@technical_path", SqliteType.Text);
            var overall = insert.Parameters.Add("@overall", SqliteType.Integer);
            var editableCount = insert.Parameters.Add("@editable_count", SqliteType.Integer);
            var iconKey = insert.Parameters.Add("@icon_key", SqliteType.Text);

            // One prepared statement reused for every row (50k rows land in
            // one commit in well under a second).
            foreach (var row in rows)
            {
                id.Value = row.Id;
                name.Value = row.Name;
                category.Value = row.Category;
                technicalPath.Value = row.TechnicalPath;
                overall.Value = (int)row.Overall;
                editableCount.Value = row.EditableCount;
                iconKey.Value = (object?)row.IconKey ?? DBNull.Value;
                insert.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    public string? ReadBuildId(string dbPath)
    {
        ValidatePath(dbPath);
        if (!File.Exists(dbPath)) return null;

        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = @key";
        cmd.Parameters.Add("@key", SqliteType.Text).Value = BuildIdKey;
        return cmd.ExecuteScalar() as string;
    }

    public DateTime? ReadLastScanUtc(string dbPath)
    {
        ValidatePath(dbPath);
        if (!File.Exists(dbPath)) return null;

        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = @key";
        cmd.Parameters.Add("@key", SqliteType.Text).Value = LastScanKey;
        var value = cmd.ExecuteScalar() as string;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;
    }

    public IReadOnlyList<string> Categories(string dbPath)
    {
        ValidatePath(dbPath);
        if (!File.Exists(dbPath)) return Array.Empty<string>();

        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT category FROM assets ORDER BY category";

        var results = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
                results.Add(reader.GetString(0));
        }
        return results;
    }

    public IReadOnlyList<AssetSummary> Query(string dbPath, string query, string? category, int skip, int take)
    {
        ValidatePath(dbPath);

        // take is clamped to [0, MaxTake]; skip to >= 0.
        if (take <= 0) return Array.Empty<AssetSummary>();
        if (take > MaxTake) take = MaxTake;
        if (skip < 0) skip = 0;

        // A read never creates the db file.
        if (!File.Exists(dbPath)) return Array.Empty<AssetSummary>();

        using var conn = Open(dbPath);

        // Fixed fragments joined together; the only interpolated values are
        // parameters (@name, @category, @take, @skip), never raw user input.
        string? namePattern = null;
        var conditions = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(query))
        {
            // LIKE is case-insensitive for ASCII (SCUM item names are ASCII);
            // %, _ and the escape character in the query match literally.
            namePattern = "%" + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            conditions.Add("name LIKE @name ESCAPE '\\'");
        }
        if (!string.IsNullOrEmpty(category))
            conditions.Add("category = @category COLLATE NOCASE");

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, category, technical_path, overall, editable_count, icon_key FROM assets"
            + (conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : string.Empty)
            + " ORDER BY name, id LIMIT @take OFFSET @skip";

        if (namePattern is not null)
            cmd.Parameters.Add("@name", SqliteType.Text).Value = namePattern;
        if (!string.IsNullOrEmpty(category))
            cmd.Parameters.Add("@category", SqliteType.Text).Value = category;
        cmd.Parameters.Add("@take", SqliteType.Integer).Value = take;
        cmd.Parameters.Add("@skip", SqliteType.Integer).Value = skip;

        var results = new List<AssetSummary>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                results.Add(new AssetSummary(
                    Id: reader.GetString(0),
                    Name: reader.GetString(1),
                    Category: reader.GetString(2),
                    Overall: (StatusKind)reader.GetInt32(4),
                    EditableCount: reader.GetInt32(5),
                    TechnicalPath: reader.GetString(3),
                    IconKey: reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }
        return results;
    }

    public int Count(string dbPath)
    {
        ValidatePath(dbPath);
        if (!File.Exists(dbPath)) return 0;

        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM assets";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public bool IsStale(string dbPath, string currentGameBuildId)
        => ReadBuildId(dbPath) != currentGameBuildId;

    private static void ValidatePath(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("Database path is required.", nameof(dbPath));
    }

    /// <summary>Opens the db (creating the file if needed) and ensures the schema.</summary>
    private static SqliteConnection Open(string dbPath)
    {
        ValidatePath(dbPath);

        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            // Close really releases the file handle: the cache file can be
            // deleted or replaced while the app runs (and tests clean up).
            Pooling = false,
        }.ToString());
        conn.Open();
        EnsureSchema(conn);
        return conn;
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        foreach (var ddl in SchemaDdl)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
