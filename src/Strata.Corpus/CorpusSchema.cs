namespace Strata.Corpus;

/// <summary>
/// The signature-DB schema (SchemaVersion 1, contracts/signature-db.md). The schema version is a public
/// contract: the CLI version-gates the corpus it loads.
/// </summary>
public static class CorpusSchema
{
    public const int SchemaVersion = 1;

    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS library (
            id            INTEGER PRIMARY KEY,
            name          TEXT NOT NULL UNIQUE,
            purl          TEXT,
            known_license TEXT
        );

        CREATE TABLE IF NOT EXISTS string_signature (
            id             INTEGER PRIMARY KEY,
            library_id     INTEGER NOT NULL REFERENCES library(id),
            value          TEXT NOT NULL,
            distinctiveness REAL NOT NULL,
            exact_version  TEXT,
            version_low    TEXT,
            version_high   TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_string_signature_value ON string_signature(value);
        CREATE INDEX IF NOT EXISTS ix_string_signature_library ON string_signature(library_id);
        """;
}
