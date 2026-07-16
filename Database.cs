using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Jotlay;

/// <summary>
/// All persistence lives here. Notes and settings are stored in a single SQLite
/// file under %AppData%\Jotlay\jotlay.db so nothing depends on where the .exe sits.
/// </summary>
public sealed class Database
{
    private readonly string _connString;
    public string DbPath { get; }

    public Database()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jotlay");
        Directory.CreateDirectory(dir);
        DbPath = Path.Combine(dir, "jotlay.db");
        _connString = new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();
        Init();
    }

    private void Init()
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS notes (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                created_utc TEXT NOT NULL,
                bucket      TEXT NOT NULL,
                body        TEXT NOT NULL,
                raw         TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_notes_bucket ON notes(bucket);
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    public void AddNote(string bucket, string body, string raw)
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO notes (created_utc, bucket, body, raw) VALUES ($t, $b, $body, $raw)";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$b", bucket);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.Parameters.AddWithValue("$raw", raw);
        cmd.ExecuteNonQuery();
    }

    public string GetSetting(string key, string fallback)
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string ?? fallback;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO settings (key, value) VALUES ($k, $v) " +
            "ON CONFLICT(key) DO UPDATE SET value = $v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public List<NoteRow> AllNotes()
    {
        var list = new List<NoteRow>();
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, created_utc, bucket, body FROM notes ORDER BY bucket, created_utc";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new NoteRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    /// <summary>Every bucket that has at least one note, with its note count, alphabetical.</summary>
    public List<BucketRow> Buckets()
    {
        var list = new List<BucketRow>();
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT bucket, COUNT(*) FROM notes GROUP BY bucket ORDER BY bucket";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BucketRow(r.GetString(0), r.GetInt32(1)));
        return list;
    }

    public List<NoteRow> NotesInBucket(string bucket)
    {
        var list = new List<NoteRow>();
        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, created_utc, bucket, body FROM notes WHERE bucket = $b ORDER BY created_utc";
        cmd.Parameters.AddWithValue("$b", bucket);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new NoteRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    /// <summary>Permanently deletes the given notes by id. Returns how many rows were removed.</summary>
    public int DeleteNotes(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return 0;

        using var conn = new SqliteConnection(_connString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        int removed = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM notes WHERE id = $id";
            var p = cmd.CreateParameter();
            p.ParameterName = "$id";
            cmd.Parameters.Add(p);
            foreach (var id in idList)
            {
                p.Value = id;
                removed += cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
        return removed;
    }
}

public readonly record struct NoteRow(long Id, string CreatedUtc, string Bucket, string Body);

public readonly record struct BucketRow(string Bucket, int Count);
