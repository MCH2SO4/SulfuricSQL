using System.Data;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace SulfuricSQL.Services;

public sealed record ColumnDefinition(string Name, string Type, string Size, bool PrimaryKey, bool Nullable, bool AutoIncrement);

// 管理功能只生成明确的 SQL，统一交给 SqlExecutionService 做检查和执行。
public sealed class DatabaseService(MySqlConnection connection)
{
    public static string Quote(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Contains('\0')) throw new ArgumentException("名称不能为空，最长 64 个字符，且不能包含空字符。");
        return DatabaseBrowserService.Quote(name);
    }
    public static string CreateDatabase(string name, string charset, string collation) => $"CREATE DATABASE {Quote(name)} CHARACTER SET {Quote(charset)} COLLATE {Quote(collation)}";
    public static string AlterDatabase(string name, string charset, string collation) => $"ALTER DATABASE {Quote(name)} CHARACTER SET {Quote(charset)} COLLATE {Quote(collation)}";
    public static string DropDatabase(string name)
    {
        if (SqlSafetyChecker.IsSystemDatabase(name)) throw new ArgumentException("禁止删除系统数据库：" + name);
        return $"DROP DATABASE {Quote(name)}";
    }
    public static string DropTable(string db, string table) => $"DROP TABLE {Quote(db)}.{Quote(table)}";
    public static string TruncateTable(string db, string table) => $"TRUNCATE TABLE {Quote(db)}.{Quote(table)}";
    public static string RenameTable(string db, string table, string newName) => $"RENAME TABLE {Quote(db)}.{Quote(table)} TO {Quote(db)}.{Quote(newName)}";

    public static string CreateTable(string database, string table, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 0) throw new ArgumentException("请至少添加一个字段。");
        if (columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count) throw new ArgumentException("字段名不能重复。");
        if (columns.Count(c => c.AutoIncrement) > 1) throw new ArgumentException("只能有一个自增字段。");
        var definitions = new List<string>();
        foreach (var column in columns)
        {
            string type = column.Type.ToUpperInvariant();
            if (!new[] { "INT", "BIGINT", "VARCHAR", "TEXT", "DECIMAL", "DOUBLE", "DATE", "DATETIME", "BOOLEAN", "BLOB" }.Contains(type)) throw new ArgumentException("不支持的数据类型：" + type);
            if (type == "VARCHAR")
            {
                if (!int.TryParse(column.Size, out int size) || size is < 1 or > 65535) throw new ArgumentException("VARCHAR 长度应为 1～65535。");
                type += "(" + size + ")";
            }
            if (type == "DECIMAL")
            {
                var match = Regex.Match(column.Size, @"^\s*(\d{1,2})\s*,\s*(\d{1,2})\s*$");
                if (!match.Success) throw new ArgumentException("DECIMAL 长度请填写精度,小数位，例如 10,2。");
                int precision = int.Parse(match.Groups[1].Value), scale = int.Parse(match.Groups[2].Value);
                if (precision is < 1 or > 65 || scale > 30 || scale > precision) throw new ArgumentException("DECIMAL 精度或小数位不合法。");
                type += $"({precision},{scale})";
            }
            if (column.PrimaryKey && column.Nullable) throw new ArgumentException("主键字段不能允许 NULL。");
            if (column.AutoIncrement && (!column.PrimaryKey || column.Type is not ("INT" or "BIGINT"))) throw new ArgumentException("自增字段需要是 INT/BIGINT 主键。");
            definitions.Add($"{Quote(column.Name)} {type} {(column.Nullable ? "NULL" : "NOT NULL")}{(column.AutoIncrement ? " AUTO_INCREMENT" : "")}");
        }
        var keys = columns.Where(c => c.PrimaryKey).ToList();
        if (columns.Any(c => c.AutoIncrement) && !keys[0].AutoIncrement) throw new ArgumentException("复合主键中的自增字段必须排在第一位。");
        if (keys.Count > 0) definitions.Add("PRIMARY KEY (" + string.Join(", ", keys.Select(c => Quote(c.Name))) + ")");
        return $"CREATE TABLE {Quote(database)}.{Quote(table)} (\n  {string.Join(",\n  ", definitions)}\n) ENGINE=InnoDB";
    }

    public async Task<DataTable> GetCollationsAsync(CancellationToken token)
    {
        using var command = new MySqlCommand("SELECT CHARACTER_SET_NAME, COLLATION_NAME, IS_DEFAULT FROM information_schema.COLLATIONS ORDER BY CHARACTER_SET_NAME, COLLATION_NAME", connection);
        return await ReadAsync(command, token);
    }

    public async Task<DataTable> GetPropertiesAsync(string name, CancellationToken token)
    {
        using var command = new MySqlCommand("SELECT s.SCHEMA_NAME AS 数据库, s.DEFAULT_CHARACTER_SET_NAME AS 字符集, s.DEFAULT_COLLATION_NAME AS 排序规则, " +
            "(SELECT COUNT(*) FROM information_schema.TABLES t WHERE t.TABLE_SCHEMA=s.SCHEMA_NAME) AS 表与视图数量, " +
            "(SELECT COALESCE(SUM(DATA_LENGTH+INDEX_LENGTH),0) FROM information_schema.TABLES t WHERE t.TABLE_SCHEMA=s.SCHEMA_NAME) AS 估计字节数 " +
            "FROM information_schema.SCHEMATA s WHERE s.SCHEMA_NAME=@name", connection);
        command.Parameters.AddWithValue("@name", name);
        return await ReadAsync(command, token);
    }

    private static async Task<DataTable> ReadAsync(MySqlCommand command, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token);
        var table = new DataTable();
        for (int i = 0; i < reader.FieldCount; i++) table.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
        while (await reader.ReadAsync(token)) { var values = new object[reader.FieldCount]; reader.GetValues(values); table.Rows.Add(values); }
        return table;
    }
}
