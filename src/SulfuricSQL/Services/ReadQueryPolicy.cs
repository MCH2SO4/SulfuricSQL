namespace SulfuricSQL.Services;

// 查询编辑器目前只支持一条查询语句，不执行修改数据的语句。
// 识别引号和注释，不能直接用 Split(';')，因为字符串里也可能有分号。
public static class ReadQueryPolicy
{
    public static void Validate(string sql, bool noBackslashEscapes = false, bool ansiQuotes = false)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("请先输入一条 SQL 查询。");
        var words = new List<string>();
        bool ended = false;
        for (int i = 0; i < sql.Length;)
        {
            char c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '#' || (c == '-' && i + 2 < sql.Length && sql[i + 1] == '-' && char.IsWhiteSpace(sql[i + 2])))
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new ArgumentException("SQL 注释没有结束。");
                if (sql.AsSpan(i + 2, end - i - 2).Contains('!'))
                    throw new ArgumentException("查询编辑器不支持可执行的版本注释。");
                i = end + 2;
                continue;
            }
            if (ended) throw new ArgumentException("一次只运行一条查询，请选择需要运行的部分。");
            if (c == ';') { ended = true; i++; continue; }
            if (c is '\'' or '"' or '`')
            {
                char quote = c;
                bool closed = false;
                i++;
                while (i < sql.Length)
                {
                    bool stringQuote = quote == '\'' || (quote == '"' && !ansiQuotes);
                    if (sql[i] == '\\' && stringQuote && !noBackslashEscapes) { i += 2; continue; }
                    if (sql[i++] != quote) continue;
                    if (i < sql.Length && sql[i] == quote) { i++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) throw new ArgumentException("SQL 引号没有结束。");
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                words.Add(sql[start..i].ToUpperInvariant());
            }
            else i++;
        }
        if (words.Count == 0 || !new[] { "SELECT", "WITH", "SHOW", "DESCRIBE", "DESC", "EXPLAIN" }.Contains(words[0]))
            throw new ArgumentException("当前支持 SELECT、WITH、SHOW、DESCRIBE 和 EXPLAIN 查询。");
        string[] forbidden = ["INSERT", "UPDATE", "DELETE", "REPLACE", "DROP", "ALTER", "TRUNCATE", "CALL", "COMMIT", "ROLLBACK", "SET", "INTO", "LOCK", "UNLOCK", "GRANT", "REVOKE", "GET_LOCK", "RELEASE_LOCK"];
        if (words.Any(forbidden.Contains))
            throw new ArgumentException("当前编辑器用于查询，不支持修改数据、文件输出或加锁语句。");
    }
}
