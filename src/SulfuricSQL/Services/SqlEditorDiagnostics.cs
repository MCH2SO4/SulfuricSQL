

namespace SulfuricSQL.Services;

public sealed record SqlEditorIssue(int Start, int Length, string Message);

// 仅检查无需访问服务器就能确定的输入问题，不把任意标识符当成关键词。
public static class SqlEditorDiagnostics
{
    private static readonly string[] Commands = "SELECT INSERT UPDATE DELETE CREATE ALTER DROP TRUNCATE SHOW DESCRIBE EXPLAIN USE WITH REPLACE SET CALL START BEGIN COMMIT ROLLBACK GRANT REVOKE ANALYZE OPTIMIZE REPAIR RENAME".Split(' ');
    public static IReadOnlyList<SqlEditorIssue> Analyze(string sql)
    {
        var issues = new List<SqlEditorIssue>();
        bool statementStart = true;
        for (int i = 0; i < sql.Length;)
        {
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }
            if (sql[i] == '#' || (i + 2 < sql.Length && sql[i..(i + 2)] == "--" && char.IsWhiteSpace(sql[i + 2])))
            { while (i < sql.Length && sql[i] != '\n') i++; continue; }
            int start = i;
            if (i + 1 < sql.Length && sql[i..(i + 2)] == "/*")
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { issues.Add(new(start, 2, "注释未结束，缺少 */。")); break; }
                i = end + 2; continue;
            }
            if (sql[i] is '\'' or '"' or '`')
            {
                char quote = sql[i++]; bool closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == '\\' && quote != '`') { i = Math.Min(sql.Length, i + 2); continue; }
                    if (sql[i++] != quote) continue;
                    if (i < sql.Length && sql[i] == quote) { i++; continue; }
                    closed = true; break;
                }
                if (!closed) issues.Add(new(start, Math.Max(1, i - start), "引号未闭合，请检查字符串或标识符。"));
                statementStart = false; continue;
            }
            if (sql[i] == ';') { statementStart = true; i++; continue; }
            if (char.IsLetter(sql[i]) || sql[i] == '_')
            {
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                string word = sql[start..i].ToUpperInvariant();
                if (statementStart && !Commands.Contains(word))
                {
                    // 未输入完的关键词暂不报错。
                    if (i < sql.Length || !Commands.Any(c => c.StartsWith(word, StringComparison.Ordinal)))
                    {
                        string? expected = Commands.FirstOrDefault(c => Near(word, c));
                        if (expected != null) issues.Add(new(start, i - start, $"可能拼错了关键词：{word}，是否应为 {expected}？"));
                    }
                }
                statementStart = false; continue;
            }
            statementStart = false; i++;
        }
        return issues;
    }
    private static bool Near(string a, string b)
    {
        if (a.Length < 3 || Math.Abs(a.Length - b.Length) > 1) return false;
        if (a.Length == b.Length)
        {
            var differences = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToArray();
            return differences.Length == 1 || (differences.Length == 2 && differences[1] == differences[0] + 1 && a[differences[0]] == b[differences[1]] && a[differences[1]] == b[differences[0]]);
        }
        string longer = a.Length > b.Length ? a : b, shorter = a.Length > b.Length ? b : a;
        return Enumerable.Range(0, longer.Length).Any(i => longer.Remove(i, 1) == shorter);
    }
}

