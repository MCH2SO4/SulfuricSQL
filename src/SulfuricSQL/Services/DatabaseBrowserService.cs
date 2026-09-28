using System.Data;
using MySqlConnector;
using SulfuricSQL.Models;

namespace SulfuricSQL.Services;

// 一次只执行一个查询；界面在等待时禁用重复操作。
public sealed class DatabaseBrowserService(MySqlConnection connection)
{
    public async Task<List<string>> GetDatabasesAsync(CancellationToken token)
    {
        using var command = new MySqlCommand("SHOW DATABASES", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var names = new List<string>();
        while (await reader.ReadAsync(token)) names.Add(reader.GetString(0));
        return names;
    }

    public async Task<List<string>> GetTablesAsync(string database, CancellationToken token)
    {
        using var command = new MySqlCommand($"SHOW FULL TABLES FROM {Quote(database)}", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var names = new List<string>();
        while (await reader.ReadAsync(token)) names.Add(reader.GetString(0));
        return names;
    }

    public async Task<(DataTable Rows, bool HasMore, bool Ordered)> ReadPageAsync(
        string database, string table, int page, int pageSize, CancellationToken token, BrowseOptions? options = null)
    {
        if (page < 0 || pageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(page));
        var keys = new List<string>();
        using (var keyCommand = new MySqlCommand(
            "SELECT COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE " +
            "WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@table AND CONSTRAINT_NAME='PRIMARY' ORDER BY ORDINAL_POSITION", connection))
        {
            keyCommand.Parameters.AddWithValue("@db", database);
            keyCommand.Parameters.AddWithValue("@table", table);
            await using var keyReader = await keyCommand.ExecuteReaderAsync(token);
            while (await keyReader.ReadAsync(token)) keys.Add(Quote(keyReader.GetString(0)));
        }
        // 复合主键也支持；数据不变时翻页顺序稳定。
        string order = keys.Count > 0 ? " ORDER BY " + string.Join(",", keys) : "";
        if (!string.IsNullOrEmpty(options?.SortColumn))
        {
            string selected = Quote(options.SortColumn);
            var tieBreakers = keys.Where(key => key != selected).ToList();
            order = " ORDER BY " + selected + (options.Descending ? " DESC" : " ASC");
            if (tieBreakers.Count > 0) order += "," + string.Join(",", tieBreakers);
        }
        bool searching = !string.IsNullOrEmpty(options?.SearchColumn) && options.SearchText.Length > 0;
        string where = searching ? $" WHERE CAST({Quote(options!.SearchColumn!)} AS CHAR) LIKE @search ESCAPE '='" : "";
        using var command = new MySqlCommand(
            $"SELECT * FROM {Quote(database)}.{Quote(table)}{where}{order} LIMIT @limit OFFSET @offset", connection);
        if (searching)
            command.Parameters.AddWithValue("@search", "%" + options!.SearchText.Replace("=", "==").Replace("%", "=%").Replace("_", "=_") + "%");
        command.Parameters.AddWithValue("@limit", pageSize + 1);
        command.Parameters.AddWithValue("@offset", checked((long)page * pageSize));
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new DataTable();
        for (int i = 0; i < reader.FieldCount; i++) rows.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
        // 多读一行判断下一页，避免为了总数扫描整张表。
        while (await reader.ReadAsync(token))
        {
            if (rows.Rows.Count == pageSize) return (rows, true, keys.Count > 0);
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Rows.Add(values);
        }
        return (rows, false, keys.Count > 0);
    }

    // 值用参数；数据库名和表名用反引号，并转义内部的反引号。
    public static string Quote(string name) => "`" + name.Replace("`", "``") + "`";

    public async Task<DataTable> GetColumnsAsync(string database, string table, CancellationToken token)
    {
        using var command = new MySqlCommand($"SHOW FULL COLUMNS FROM {Quote(database)}.{Quote(table)}", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new DataTable();
        foreach (string title in new[] { "字段名", "数据类型", "允许空值", "索引", "默认值", "说明" }) result.Columns.Add(title);
        while (await reader.ReadAsync(token))
            result.Rows.Add(reader["Field"], reader["Type"], reader["Null"], reader["Key"], reader["Default"], reader["Comment"]);
        return result;
    }
}
