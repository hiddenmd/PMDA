using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Formats.Decoding;
using PMDA.Formats.Projects;
using PMDA.Formats.Recipes;

namespace PMDA.Data.Formats;

/// <summary>
/// Generates and fills the typed tables of header-defined projects: one table per stored message in the project's
/// schema, one child table per <c>&lt;SubStructure&gt;</c> array, and the raw record store. Tables only grow: a new
/// header version adds nullable columns, so one table serves every version and existing reports keep working.
/// </summary>
public sealed class SqlFormatRowSink(string connectionString, SemaphoreSlim? writeGate = null, int batchRows = 20_000) : IFormatRowSink
{
    private readonly Dictionary<MessagePlan, TableBuffer> _tables = new(ReferenceEqualityComparer.Instance);
    private DataTable? _raw;
    private string _schema = "";
    private int _version;

    public static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public async Task<PrepareResult> PrepareAsync(CompiledFormat format, int version, string headerText, CancellationToken ct)
    {
        _schema = format.SqlSchema;
        _version = version;
        var report = new List<string>();
        var problems = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "IF SCHEMA_ID(@s) IS NULL EXEC(N'CREATE SCHEMA ' + @q);", new { s = _schema, q = Quote(_schema) }, cancellationToken: ct));

        foreach (var plan in format.StoredTables)
        {
            var columns = new List<(string Name, string Type)>
            {
                ("FlightId", "uniqueidentifier NOT NULL"), ("RecordSeq", "bigint NOT NULL"), ("TimeUtc", "datetime2(7) NOT NULL"),
                ("EventId", "int NOT NULL"), ("FormatVersion", "int NOT NULL"),
            };
            columns.AddRange(plan.Columns.Select(c => (c.Name, SqlType(c) + " NULL")));
            await EnsureTableAsync(conn, plan.TableName, columns, format.Project.Id, plan.Key, null, report, problems, ct);

            foreach (var child in plan.Children)
            {
                var childColumns = new List<(string, string)> { ("FlightId", "uniqueidentifier NOT NULL"), ("RecordSeq", "bigint NOT NULL"), ("Idx", "int NOT NULL") };
                childColumns.AddRange(child.Columns.Select(c => (c.Name, SqlType(c) + " NULL")));
                await EnsureTableAsync(conn, ChildTable(plan, child), childColumns, format.Project.Id, plan.Key, plan.TableName, report, problems, ct);
            }
        }

        var sha = SHA256.HashData(Encoding.UTF8.GetBytes(headerText));
        await conn.ExecuteAsync(new CommandDefinition("""
            IF NOT EXISTS (SELECT 1 FROM dbo.FormatVersion WHERE ProjectId = @ProjectId AND Version = @Version)
                INSERT dbo.FormatVersion (ProjectId, Version, Sha256, HeaderText, ProjectJson)
                VALUES (@ProjectId, @Version, @Sha, @HeaderText, @ProjectJson);
            """,
            new { ProjectId = format.Project.Id, Version = version, Sha = sha, HeaderText = headerText, ProjectJson = JsonSerializer.Serialize(format.Project, FormatProject.Json) },
            cancellationToken: ct));
        return new PrepareResult(report, problems);
    }

    public static string ChildTable(MessagePlan plan, ChildDef child) => $"{plan.TableName}_{child.Name}";

    private async Task EnsureTableAsync(SqlConnection conn, string table, List<(string Name, string Type)> columns, string projectId, string messageKey,
        string? parent, List<string> report, List<string> problems, CancellationToken ct)
    {
        var full = $"{Quote(_schema)}.{Quote(table)}";
        var existing = (await conn.QueryAsync<(string Name, string Type)>(new CommandDefinition("""
            SELECT c.name, t.name
            FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@full);
            """, new { full }, cancellationToken: ct))).ToDictionary(c => c.Name, c => c.Type, StringComparer.OrdinalIgnoreCase);

        if (existing.Count == 0)
        {
            var sql = new StringBuilder($"CREATE TABLE {full} (");
            sql.AppendJoin(", ", columns.Select(c => $"{Quote(c.Name)} {c.Type}"));
            sql.Append($"); CREATE CLUSTERED COLUMNSTORE INDEX {Quote("CCI_" + table)} ON {full};");
            await conn.ExecuteAsync(new CommandDefinition(sql.ToString(), commandTimeout: 0, cancellationToken: ct));
            report.Add($"Created {_schema}.{table} ({columns.Count} columns).");
        }
        else
        {
            var added = new List<string>();
            foreach (var (name, type) in columns)
            {
                if (existing.TryGetValue(name, out var have))
                {
                    var want = type.Split(' ', '(')[0];
                    if (!have.Equals(want, StringComparison.OrdinalIgnoreCase))
                        problems.Add($"{_schema}.{table}.{name} is {have} in the database but {want} in this header; the existing type is kept.");
                    continue;
                }
                // New columns of an existing table must be nullable: older flights have no value.
                var nullable = type.Replace(" NOT NULL", " NULL", StringComparison.Ordinal);
                await conn.ExecuteAsync(new CommandDefinition($"ALTER TABLE {full} ADD {Quote(name)} {nullable};", commandTimeout: 0, cancellationToken: ct));
                added.Add(name);
            }
            if (added.Count > 0)
                report.Add($"Added {added.Count} column(s) to {_schema}.{table}: {string.Join(", ", added)}.");
        }

        await conn.ExecuteAsync(new CommandDefinition("""
            IF NOT EXISTS (SELECT 1 FROM dbo.FormatTable WHERE SchemaName = @schema AND TableName = @table)
                INSERT dbo.FormatTable (SchemaName, TableName, ProjectId, MessageKey, ParentTable) VALUES (@schema, @table, @projectId, @messageKey, @parent);
            """, new { schema = _schema, table, projectId, messageKey, parent }, cancellationToken: ct));
    }

    private static string SqlType(ColumnDef c) => c.Type switch
    {
        ColumnType.Int => "int",
        ColumnType.BigInt => "bigint",
        ColumnType.Float => "float",
        _ => $"nvarchar({Math.Clamp(c.TextLength, 1, 4000)})",
    };

    private static Type ClrType(ColumnDef c) => c.Type switch
    {
        ColumnType.Int => typeof(int),
        ColumnType.BigInt => typeof(long),
        ColumnType.Float => typeof(double),
        _ => typeof(string),
    };

    public async ValueTask AddAsync(Guid flightId, OutputRow row, CancellationToken ct)
    {
        if (row.Plan.Columns.Count == 0 && row.Plan.Children.Count == 0)
            return;
        if (!_tables.TryGetValue(row.Plan, out var buffer))
            _tables[row.Plan] = buffer = new TableBuffer(row.Plan, $"{Quote(_schema)}.{Quote(row.Plan.TableName)}", _schema);

        var r = buffer.Main.NewRow();
        r[0] = flightId;
        r[1] = row.RecordSeq;
        r[2] = row.TimeUtc;
        r[3] = row.EventId;
        r[4] = _version;
        for (var i = 0; i < row.Values.Length; i++)
            r[5 + i] = row.Values[i] ?? DBNull.Value;
        buffer.Main.Rows.Add(r);

        for (var c = 0; c < row.Children.Length; c++)
        {
            if (row.Children[c] is not { } rows)
                continue;
            var table = buffer.Children[c];
            for (var i = 0; i < rows.Count; i++)
            {
                var cr = table.NewRow();
                cr[0] = flightId;
                cr[1] = row.RecordSeq;
                cr[2] = i;
                for (var j = 0; j < rows[i].Length; j++)
                    cr[3 + j] = rows[i][j] ?? DBNull.Value;
                table.Rows.Add(cr);
            }
        }

        if (buffer.Main.Rows.Count >= batchRows || buffer.Children.Any(t => t.Rows.Count >= batchRows * 4))
            await buffer.FlushAsync(this, ct);
    }

    public async ValueTask AddRawAsync(Guid flightId, long seq, DateTime timeUtc, int eventId, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        _raw ??= RawTable();
        _raw.Rows.Add(flightId, seq, timeUtc, eventId, payload.ToArray());
        if (_raw.Rows.Count >= batchRows)
            await FlushRawAsync(ct);
    }

    public async Task FlushAsync(CancellationToken ct)
    {
        foreach (var buffer in _tables.Values)
            await buffer.FlushAsync(this, ct);
        await FlushRawAsync(ct);
    }

    private async Task FlushRawAsync(CancellationToken ct)
    {
        if (_raw is not { Rows.Count: > 0 })
            return;
        await BulkAsync("dbo.RawRecord", _raw, ct);
        _raw.Clear();
    }

    private static DataTable RawTable()
    {
        var t = new DataTable();
        t.Columns.Add("FlightId", typeof(Guid));
        t.Columns.Add("RecordSeq", typeof(long));
        t.Columns.Add("TimeUtc", typeof(DateTime));
        t.Columns.Add("EventId", typeof(int));
        t.Columns.Add("Bytes", typeof(byte[]));
        return t;
    }

    private async Task BulkAsync(string destination, DataTable table, CancellationToken ct)
    {
        if (writeGate is not null)
            await writeGate.WaitAsync(ct);
        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            using var bulk = new SqlBulkCopy(conn) { DestinationTableName = destination, BulkCopyTimeout = 0, BatchSize = 0 };
            foreach (DataColumn c in table.Columns)
                bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }
        finally
        {
            writeGate?.Release();
        }
    }

    public async Task DeleteFlightAsync(Guid flightId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.ExecuteAsync(new CommandDefinition(DeleteFlightSql, new { id = flightId }, commandTimeout: 0, cancellationToken: ct));
    }

    /// <summary>Deletes one flight (<c>@id</c>) from every generated table and the raw store.</summary>
    public const string DeleteFlightSql = """
        DECLARE @sql nvarchar(max) = N'';
        IF OBJECT_ID(N'dbo.FormatTable', N'U') IS NOT NULL
            SELECT @sql += N'DELETE ' + QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N' WHERE FlightId = @id;'
            FROM dbo.FormatTable WHERE OBJECT_ID(QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName)) IS NOT NULL;
        IF OBJECT_ID(N'dbo.RawRecord', N'U') IS NOT NULL
            SET @sql += N'DELETE dbo.RawRecord WHERE FlightId = @id;';
        IF LEN(@sql) > 0
            EXEC sp_executesql @sql, N'@id uniqueidentifier', @id;
        """;

    /// <summary>Empties every generated table and the raw store.</summary>
    public const string TruncateAllSql = """
        DECLARE @sql nvarchar(max) = N'';
        IF OBJECT_ID(N'dbo.FormatTable', N'U') IS NOT NULL
            SELECT @sql += N'TRUNCATE TABLE ' + QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N';'
            FROM dbo.FormatTable WHERE OBJECT_ID(QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName)) IS NOT NULL;
        IF OBJECT_ID(N'dbo.RawRecord', N'U') IS NOT NULL
            SET @sql += N'TRUNCATE TABLE dbo.RawRecord;';
        IF LEN(@sql) > 0
            EXEC sp_executesql @sql;
        """;

    private sealed class TableBuffer
    {
        private readonly string _destination;
        private readonly string[] _childDestinations;

        public TableBuffer(MessagePlan plan, string destination, string schema)
        {
            _destination = destination;
            Main = new DataTable();
            Main.Columns.Add("FlightId", typeof(Guid));
            Main.Columns.Add("RecordSeq", typeof(long));
            Main.Columns.Add("TimeUtc", typeof(DateTime));
            Main.Columns.Add("EventId", typeof(int));
            Main.Columns.Add("FormatVersion", typeof(int));
            foreach (var c in plan.Columns)
                Main.Columns.Add(c.Name, ClrType(c));
            Children = new DataTable[plan.Children.Count];
            _childDestinations = new string[plan.Children.Count];
            for (var i = 0; i < plan.Children.Count; i++)
            {
                var child = plan.Children[i];
                var t = new DataTable();
                t.Columns.Add("FlightId", typeof(Guid));
                t.Columns.Add("RecordSeq", typeof(long));
                t.Columns.Add("Idx", typeof(int));
                foreach (var c in child.Columns)
                    t.Columns.Add(c.Name, ClrType(c));
                Children[i] = t;
                _childDestinations[i] = $"{Quote(schema)}.{Quote(ChildTable(plan, child))}";
            }
        }

        public DataTable Main { get; }
        public DataTable[] Children { get; }

        public async Task FlushAsync(SqlFormatRowSink sink, CancellationToken ct)
        {
            if (Main.Rows.Count > 0)
                await sink.BulkAsync(_destination, Main, ct);
            Main.Clear();
            for (var i = 0; i < Children.Length; i++)
            {
                if (Children[i].Rows.Count > 0)
                    await sink.BulkAsync(_childDestinations[i], Children[i], ct);
                Children[i].Clear();
            }
        }
    }
}
