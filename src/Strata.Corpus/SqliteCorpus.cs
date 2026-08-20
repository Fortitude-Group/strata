using System.Globalization;
using Microsoft.Data.Sqlite;
using Strata.Core;
using Strata.Core.Corpus;
using Strata.Core.Errors;

namespace Strata.Corpus;

/// <summary>
/// Reads a signature corpus from a SQLite file and hydrates it into an in-memory <see cref="ICorpus"/>
/// (seed corpus is small enough). Version-gates the schema (contracts/signature-db.md).
/// </summary>
public static class SqliteCorpus
{
    public static ICorpus Load(string dbPath)
    {
        if (!System.IO.File.Exists(dbPath))
        {
            throw new UnsupportedFormatException($"Corpus database not found: {dbPath}");
        }

        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly };
        using var conn = new SqliteConnection(csb.ConnectionString);
        conn.Open();

        Dictionary<string, string> meta = ReadMeta(conn);
        int schema = ParseInt(meta.GetValueOrDefault("schema_version"), 0);
        if (schema != StrataInfo.SupportedCorpusSchemaVersion)
        {
            throw new CorpusSchemaMismatchException(schema, StrataInfo.SupportedCorpusSchemaVersion);
        }

        var signatures = new List<CorpusStringSignature>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT l.name, l.purl, l.known_license,
                       s.value, s.distinctiveness, s.exact_version, s.version_low, s.version_high
                FROM string_signature s
                JOIN library l ON l.id = s.library_id
                ORDER BY l.name, s.value;
                """;
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                signatures.Add(new CorpusStringSignature
                {
                    LibraryName = r.GetString(0),
                    Purl = r.IsDBNull(1) ? null : r.GetString(1),
                    KnownLicense = r.IsDBNull(2) ? null : r.GetString(2),
                    Value = r.GetString(3),
                    Distinctiveness = r.GetDouble(4),
                    ExactVersion = r.IsDBNull(5) ? null : r.GetString(5),
                    VersionLow = r.IsDBNull(6) ? null : r.GetString(6),
                    VersionHigh = r.IsDBNull(7) ? null : r.GetString(7),
                });
            }
        }

        var functionSignatures = new List<CorpusFunctionSignature>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT l.name, f.function_name, f.cfg_shape_hash, f.norm_insn_minhash, f.distinctiveness,
                       f.exact_version, f.version_low, f.version_high
                FROM function_signature f
                JOIN library l ON l.id = f.library_id
                ORDER BY l.name, f.function_name;
                """;
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                functionSignatures.Add(new CorpusFunctionSignature
                {
                    LibraryName = r.GetString(0),
                    FunctionName = r.GetString(1),
                    CfgShapeHash = ulong.Parse(r.GetString(2), CultureInfo.InvariantCulture),
                    NormInsnMinHash = ParseMinHash(r.GetString(3)),
                    Distinctiveness = r.GetDouble(4),
                    ExactVersion = r.IsDBNull(5) ? null : r.GetString(5),
                    VersionLow = r.IsDBNull(6) ? null : r.GetString(6),
                    VersionHigh = r.IsDBNull(7) ? null : r.GetString(7),
                });
            }
        }

        var manifest = new CorpusManifest
        {
            CorpusVersion = meta.GetValueOrDefault("corpus_version", "0.0.0"),
            SchemaVersion = schema,
            LibraryCount = ParseInt(meta.GetValueOrDefault("library_count"), 0),
            ModelVersion = meta.GetValueOrDefault("model_version"),
        };

        return new InMemoryCorpus(manifest, signatures, functionSignatures);
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection conn)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM meta;";
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            meta[r.GetString(0)] = r.GetString(1);
        }

        return meta;
    }

    private static int ParseInt(string? s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static uint[] ParseMinHash(string csv)
    {
        if (string.IsNullOrEmpty(csv))
        {
            return [];
        }

        string[] parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var values = new uint[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = uint.Parse(parts[i], CultureInfo.InvariantCulture);
        }

        return values;
    }
}
