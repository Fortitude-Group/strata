using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Strata.Core.Corpus;

namespace Strata.Corpus;

/// <summary>
/// Writes a signature corpus to a SQLite file (contracts/signature-db.md). Used by the seed generator
/// (T021) and, later, by the full corpus builder (T063). Deterministic: identical inputs produce an
/// equivalent DB (SC-009), so no timestamps or host paths are written into signatures.
/// </summary>
public static class CorpusWriter
{
    public static void Write(
        string dbPath,
        string corpusVersion,
        IReadOnlyList<CorpusStringSignature> signatures,
        string? modelVersion = null)
    {
        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath };
        using var conn = new SqliteConnection(csb.ConnectionString);
        conn.Open();

        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL;";
            pragma.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();

        using (var create = conn.CreateCommand())
        {
            create.CommandText = CorpusSchema.CreateSql;
            create.ExecuteNonQuery();
        }

        var libraryIds = new Dictionary<string, long>(System.StringComparer.Ordinal);
        long nextLibraryId = 1;

        foreach (CorpusStringSignature sig in signatures)
        {
            if (!libraryIds.TryGetValue(sig.LibraryName, out long libId))
            {
                libId = nextLibraryId++;
                libraryIds[sig.LibraryName] = libId;
                using var libCmd = conn.CreateCommand();
                libCmd.CommandText =
                    "INSERT INTO library (id, name, purl, known_license) VALUES ($id, $name, $purl, $lic);";
                libCmd.Parameters.AddWithValue("$id", libId);
                libCmd.Parameters.AddWithValue("$name", sig.LibraryName);
                libCmd.Parameters.AddWithValue("$purl", (object?)sig.Purl ?? System.DBNull.Value);
                libCmd.Parameters.AddWithValue("$lic", (object?)sig.KnownLicense ?? System.DBNull.Value);
                libCmd.ExecuteNonQuery();
            }

            using var sigCmd = conn.CreateCommand();
            sigCmd.CommandText = """
                INSERT INTO string_signature
                    (library_id, value, distinctiveness, exact_version, version_low, version_high)
                VALUES ($lib, $val, $dist, $exact, $low, $high);
                """;
            sigCmd.Parameters.AddWithValue("$lib", libId);
            sigCmd.Parameters.AddWithValue("$val", sig.Value);
            sigCmd.Parameters.AddWithValue("$dist", sig.Distinctiveness);
            sigCmd.Parameters.AddWithValue("$exact", (object?)sig.ExactVersion ?? System.DBNull.Value);
            sigCmd.Parameters.AddWithValue("$low", (object?)sig.VersionLow ?? System.DBNull.Value);
            sigCmd.Parameters.AddWithValue("$high", (object?)sig.VersionHigh ?? System.DBNull.Value);
            sigCmd.ExecuteNonQuery();
        }

        WriteMeta(conn, "corpus_version", corpusVersion);
        WriteMeta(conn, "schema_version", CorpusSchema.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteMeta(conn, "library_count", libraryIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteMeta(conn, "model_version", modelVersion ?? "parked");

        tx.Commit();
    }

    private static void WriteMeta(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($k, $v);";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }
}
