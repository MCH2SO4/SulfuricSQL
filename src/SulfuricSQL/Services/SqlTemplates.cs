namespace SulfuricSQL.Services;

public static class SqlTemplates
{
    public static Dictionary<string, string> Create(string? database, string? table)
    {
        string db = DatabaseBrowserService.Quote(database ?? "school");
        string name = DatabaseBrowserService.Quote(table ?? "student");
        string full = db + "." + name;
        return new()
        {
            ["SHOW DATABASES · 数据库列表"] = "SHOW DATABASES;",
            ["CREATE DATABASE · 创建数据库"] = "CREATE DATABASE `new_database` CHARACTER SET utf8mb4;",
            ["USE · 切换数据库"] = $"USE {db};",
            ["SHOW TABLES · 表列表"] = $"SHOW TABLES FROM {db};",
            ["DESCRIBE · 表结构"] = $"DESCRIBE {full};",
            ["CREATE TABLE · 创建表"] = $"CREATE TABLE {db}.`new_table` (\n  id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,\n  name VARCHAR(100)\n) ENGINE=InnoDB;",
            ["SELECT · 查询数据"] = $"SELECT * FROM {full} LIMIT 100;",
            ["INSERT · 添加数据"] = $"INSERT INTO {full} (name) VALUES ('示例');",
            ["UPDATE · 修改数据"] = $"UPDATE {full} SET name = '新值' WHERE id = 1;",
            ["DELETE · 删除数据"] = $"DELETE FROM {full} WHERE id = 1;",
            ["DROP TABLE · 删除表"] = $"DROP TABLE {full};",
            ["DROP DATABASE · 删除数据库"] = $"DROP DATABASE {db};",
            ["TRUNCATE · 清空表"] = $"TRUNCATE TABLE {full};",
            ["SELECT VERSION · 服务器版本"] = "SELECT VERSION();",
            ["SHOW VARIABLES · 服务器变量"] = "SHOW VARIABLES;",
            ["EXPLAIN · 执行计划"] = $"EXPLAIN SELECT * FROM {full} WHERE id = 1;"
        };
    }
}
