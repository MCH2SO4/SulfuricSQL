namespace SulfuricSQL.Models;

// 只保存已经点击“应用”的条件；翻页时重复使用同一份条件。
public sealed class BrowseOptions
{
    public string? SearchColumn { get; init; }
    public string SearchText { get; init; } = "";
    public string? SortColumn { get; init; }
    public bool Descending { get; init; }
}
