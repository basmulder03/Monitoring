using Microsoft.Data.Sqlite;

namespace Monitoring.Controller.Features.StateStore;

/// <summary>Keeps the logs in a SQLite file, so they survive a restart of the container when the file is on a volume.</summary>
/// <remarks>
/// One connection is kept open and used one call at a time, which is plenty for what is stored: a few writes per minute. A file that cannot be written does not take the controller down; the failure
/// is reported to the callback given to the constructor and the value is still kept for the life of the process.
/// </remarks>
internal sealed class SqliteStateStore : IStateStore, IDisposable
{
    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;
    private readonly MemoryStateStore _fallback = new();
    private readonly Action<string> _onFailure;

    /// <summary>Opens the file, creating it and its table when they are missing.</summary>
    /// <param name="path">The file.</param>
    /// <param name="onFailure">Told why the file could not be used.</param>
    public SqliteStateStore(string path, Action<string> onFailure)
    {
        _onFailure = onFailure;
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        Execute("CREATE TABLE IF NOT EXISTS log (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, json TEXT NOT NULL)");
        Execute("CREATE INDEX IF NOT EXISTS log_name ON log (name, id)");
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Read(string log)
    {
        lock (_gate)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT json FROM log WHERE name = $name ORDER BY id";
                command.Parameters.AddWithValue("$name", log);
                using var reader = command.ExecuteReader();
                var values = new List<string>();
                while (reader.Read())
                {
                    values.Add(reader.GetString(0));
                }

                return values;
            }
            catch (SqliteException ex)
            {
                _onFailure($"Cannot read the state store: {ex.Message}");
                return _fallback.Read(log);
            }
        }
    }

    /// <inheritdoc />
    public void Append(string log, string json, int keep)
    {
        lock (_gate)
        {
            try
            {
                using var insert = _connection.CreateCommand();
                insert.CommandText = "INSERT INTO log (name, json) VALUES ($name, $json)";
                insert.Parameters.AddWithValue("$name", log);
                insert.Parameters.AddWithValue("$json", json);
                insert.ExecuteNonQuery();

                using var trim = _connection.CreateCommand();
                trim.CommandText = "DELETE FROM log WHERE name = $name AND id <= (SELECT id FROM log WHERE name = $name ORDER BY id DESC LIMIT 1 OFFSET $keep)";
                trim.Parameters.AddWithValue("$name", log);
                trim.Parameters.AddWithValue("$keep", keep);
                trim.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                _onFailure($"Cannot write the state store: {ex.Message}");
                _fallback.Append(log, json, keep);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
