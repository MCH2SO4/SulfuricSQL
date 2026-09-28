using System.Text.RegularExpressions;

namespace SulfuricSQL.Services;

public sealed record ServerCompatibility(string Display, Version? Version, bool IsMySql)
{
    public static ServerCompatibility Parse(string serverVersion)
    {
        var match = Regex.Match(serverVersion, @"\d+\.\d+\.\d+");
        Version? version = match.Success && System.Version.TryParse(match.Value, out var parsed) ? parsed : null;
        bool mysql = version != null && !serverVersion.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
        return new(serverVersion, version, mysql);
    }
    public string Summary => !IsMySql ? "服务器版本规则未覆盖" : Version! < new Version(8, 0) ? "经典版本 · 基础 SQL" : "现代版本 · MySQL 8+";
}

public sealed record CompatibilityIssue(string Feature, string Message, bool Blocking);

// 复用安全检查的词法结果：忽略注释、字符串，不把标识符误当作关键词。
public static class SqlCompatibilityChecker
{
    public static IReadOnlyList<CompatibilityIssue> Check(ServerCompatibility server, IReadOnlyList<SqlStatement> statements)
    {
        var issues = new List<CompatibilityIssue>();
        if (!server.IsMySql || server.Version == null) return issues;
        var version = server.Version;
        void Add(string feature, string message, bool blocking) { if (!issues.Any(i => i.Feature == feature)) issues.Add(new(feature, message, blocking)); }
        foreach (var statement in statements)
        {
            var tokens = statement.Tokens;
            bool Word(int i, string word) => i >= 0 && i < tokens.Count && tokens[i].Keyword && tokens[i].Value == word;
            if ((Word(0, "WITH") || (Word(0, "EXPLAIN") && Word(1, "WITH"))) && version < new Version(8, 0))
                Add("WITH / CTE", "当前服务器不支持 WITH 公用表表达式（需要 MySQL 8.0+）。请改用子查询或临时表。", true);
            for (int i = 0; i < tokens.Count; i++)
            {
                string next = i + 1 < tokens.Count ? tokens[i + 1].Value : "";
                if (Word(i, "OVER") && next == "(" && version < new Version(8, 0))
                    Add("窗口函数", "OVER 窗口函数需要 MySQL 8.0+；请使用分组查询或子查询改写。", true);
                if (Word(i, "COLLATE") && next.StartsWith("utf8mb4_0900_", StringComparison.OrdinalIgnoreCase) && version < new Version(8, 0))
                    Add("排序规则", "utf8mb4_0900_* 属于 MySQL 8.0 系列排序规则。请从当前服务器的建库窗口选择支持的规则，例如 utf8mb4_unicode_ci。", true);
                if (statement.Kind is "CREATE" or "ALTER" && Word(i, "CHECK") && next == "(" && version < new Version(8, 0, 16))
                    Add("CHECK 约束", "MySQL 8.0.16 之前不执行 CHECK 约束；部分旧语法会被接受但忽略。请勿依赖它保护数据。", false);
                if (statement.Kind is "INSERT" or "REPLACE" && Word(i, "VALUES") && next == "(" && tokens.Take(i).Any(t => t.Keyword && t.Value == "DUPLICATE") && version >= new Version(8, 0, 20))
                    Add("VALUES() 旧写法", "ON DUPLICATE KEY UPDATE 中的 VALUES(列) 从 MySQL 8.0.20 起弃用。建议改用新行别名；本次不会自动改写。", false);
                if (!Word(i, "SELECT")) continue;
                // 仅匹配 SELECT 后的修饰词，避免把字段名、别名作为兼容错误。
                for (int j = i + 1; j < tokens.Count && tokens[j].Keyword && new[] { "ALL", "DISTINCT", "DISTINCTROW", "HIGH_PRIORITY", "STRAIGHT_JOIN", "SQL_SMALL_RESULT", "SQL_BIG_RESULT", "SQL_BUFFER_RESULT", "SQL_CACHE", "SQL_NO_CACHE", "SQL_CALC_FOUND_ROWS" }.Contains(tokens[j].Value); j++)
                {
                    if (Word(j, "SQL_CACHE") && version >= new Version(8, 0)) Add("SQL_CACHE", "SQL_CACHE 是旧查询缓存写法，MySQL 8.0 已移除。请删除该修饰词后重试。", true);
                    if (Word(j, "SQL_NO_CACHE") && version >= new Version(8, 0)) Add("SQL_NO_CACHE", "MySQL 8.0 已移除查询缓存，SQL_NO_CACHE 已弃用且不再起作用。可删除这个修饰词。", false);
                    if (Word(j, "SQL_CALC_FOUND_ROWS") && version >= new Version(8, 0, 17)) Add("SQL_CALC_FOUND_ROWS", "此旧写法从 MySQL 8.0.17 起弃用。建议使用 LIMIT 查询，再单独用 COUNT(*) 统计总数。", false);
                }
            }
        }
        return issues;
    }
}
