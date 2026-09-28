namespace SulfuricSQL.Services;

public sealed record SqlToken(string Value, bool Keyword, int Depth);
public sealed record SqlStatement(string Text, string Kind, string? Risk, bool ChangesSchema, bool RequiresDatabase)
{
    public IReadOnlyList<SqlToken> Tokens { get; init; } = [];
}

// 轻量词法检查，不用正则搜索 WHERE：注释、字符串和子查询里的 WHERE 不算外层条件。
public static class SqlSafetyChecker
{

    public static bool IsSystemDatabase(string name)
    {
        name = name.TrimEnd();
        if (name.StartsWith("#mysql50#", StringComparison.OrdinalIgnoreCase)) name = name[9..];
        return new[] { "information_schema", "mysql", "performance_schema", "sys" }.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public static List<SqlStatement> Analyze(string sql, bool noBackslashEscapes = false, bool ansiQuotes = false)
    {
        var result = new List<SqlStatement>();
        var tokens = new List<SqlToken>();
        int start = 0, depth = 0;
        void Finish(int end)
        {
            if (tokens.Count == 0) return;
            if (depth != 0) throw new ArgumentException("SQL 括号没有配对。");
            var top = tokens.Where(t => t.Depth == 0).ToList();
            string kind = top.FirstOrDefault()?.Value.ToUpperInvariant() ?? "";
            if (kind == "WITH") kind = top.Skip(1).FirstOrDefault(t => t.Keyword && new[] { "SELECT", "UPDATE", "DELETE", "INSERT", "REPLACE" }.Contains(t.Value))?.Value ?? "";
            if (!new[] { "SELECT", "SHOW", "DESCRIBE", "DESC", "EXPLAIN", "CREATE", "ALTER", "INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "USE", "RENAME", "REPLACE" }.Contains(kind))
                throw new ArgumentException("当前支持常用查询、库表管理和数据增删改；不支持 DELIMITER、动态 SQL、存储程序或会话设置脚本。");
            if (kind is "CREATE" or "ALTER" or "DROP")
            {
                string type = top.Skip(1).FirstOrDefault(t => t.Keyword && t.Value is not ("TEMPORARY" or "UNIQUE" or "FULLTEXT" or "SPATIAL"))?.Value ?? "";
                if (!new[] { "DATABASE", "SCHEMA", "TABLE", "INDEX", "VIEW" }.Contains(type))
                    throw new ArgumentException("这里只支持数据库、表、索引和视图管理。");
            }
            string? risk = null;
            if (kind == "DROP")
            {
                risk = "DROP 会删除数据库对象及其数据，通常无法撤销。";
                if (top.Count > 1 && top[1].Keyword && top[1].Value is "DATABASE" or "SCHEMA")
                {
                    int nameIndex = 2;
                    if (top.Count > 3 && top[2].Keyword && top[2].Value == "IF" && top[3].Keyword && top[3].Value == "EXISTS") nameIndex = 4;
                    if (top.Count != nameIndex + 1) throw new ArgumentException("DROP DATABASE 语法无法可靠识别，请使用 DROP DATABASE 数据库名。");
                    if (IsSystemDatabase(top[nameIndex].Value)) throw new ArgumentException("禁止删除系统数据库：" + top[nameIndex].Value);
                }
            }
            if (kind == "TRUNCATE") risk = "TRUNCATE 会清空整张表并重置自增计数，通常无法撤销。";
            // db.where / t.where 和 @where 是标识符或变量，不是 WHERE 子句。
            bool hasWhere = top.Where((t, index) => t.Keyword && t.Value == "WHERE" &&
                (index == 0 || top[index - 1].Value is not ("." or "@"))).Any();
            if (kind is "DELETE" or "UPDATE" && !hasWhere)
                risk = kind + " 没有外层 WHERE，可能影响整张表。";
            bool serverOnly = kind == "USE" ||
                (kind == "SELECT" && !tokens.Any(t => t.Keyword && t.Value == "FROM")) ||
                (kind == "SHOW" && top.Count > 1 && new[] { "DATABASES", "SCHEMAS", "VARIABLES", "STATUS", "GLOBAL", "SESSION", "CHARACTER", "CHARSET", "COLLATION", "ENGINES", "PROCESSLIST" }.Contains(top[1].Value)) ||
                (kind is "CREATE" or "ALTER" or "DROP" && top.Count > 1 && top[1].Value is "DATABASE" or "SCHEMA");
            result.Add(new SqlStatement(sql[start..end].Trim(), kind, risk, kind is "CREATE" or "ALTER" or "DROP" or "TRUNCATE" or "RENAME", !serverOnly) { Tokens = tokens.ToArray() });
            tokens.Clear();
        }
        for (int i = 0; i < sql.Length;)
        {
            char c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#' || (c == '-' && i + 2 < sql.Length && sql[i + 1] == '-' && (char.IsWhiteSpace(sql[i + 2]) || char.IsControl(sql[i + 2]))))
            { while (i < sql.Length && sql[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new ArgumentException("SQL 注释没有结束。");
                if (sql.AsSpan(i + 2, end - i - 2).Contains('!')) throw new ArgumentException("不支持可执行的版本注释，请改写为普通 SQL。");
                i = end + 2; continue;
            }
            if (c == ';') { Finish(i); start = ++i; continue; }
            if (c is '\'' or '"' or '`')
            {
                char quote = c; bool closed = false;
                bool identifier = quote == '`' || (quote == '"' && ansiQuotes);
                var value = new System.Text.StringBuilder();
                i++;
                while (i < sql.Length)
                {
                    char next = sql[i++];
                    if (next == '\\' && !identifier && !noBackslashEscapes && i < sql.Length) { value.Append(sql[i++]); continue; }
                    if (next != quote) { value.Append(next); continue; }
                    if (i < sql.Length && sql[i] == quote) { value.Append(quote); i++; continue; }
                    closed = true; break;
                }
                if (!closed) throw new ArgumentException("SQL 引号没有结束。");
                tokens.Add(new SqlToken(identifier ? value.ToString() : "<字符串>", false, depth));
                continue;
            }
            if (c == '(') { tokens.Add(new SqlToken("(", false, depth)); depth++; i++; continue; }
            if (c == ')') { if (--depth < 0) throw new ArgumentException("SQL 括号没有配对。"); tokens.Add(new SqlToken(")", false, depth)); i++; continue; }
            if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                int begin = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '$')) i++;
                tokens.Add(new SqlToken(sql[begin..i].ToUpperInvariant(), true, depth));
            }
            else { tokens.Add(new SqlToken(c.ToString(), false, depth)); i++; }
        }
        Finish(sql.Length);
        if (result.Count == 0) throw new ArgumentException("请先输入 SQL。");
        return result;
    }
}

