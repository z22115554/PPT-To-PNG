using System.Text;

namespace PptPngExporter.Core.Parsing;

/// <summary>使用者輸入的頁碼格式不正確時擲出。Message 為可直接顯示給使用者的繁體中文說明。</summary>
public sealed class PageRangeFormatException : Exception
{
    public PageRangeFormatException(string message) : base(message) { }
}

/// <summary>單一頁碼區段。<see cref="End"/> 為 null 代表「到最後一頁」。</summary>
public readonly record struct PageInterval(int Start, int? End)
{
    public override string ToString() => End is null ? $"{Start}-" : Start == End ? $"{Start}" : $"{Start}-{End}";
}

/// <summary>
/// 已解析完成的頁碼選取條件。與簡報總頁數無關，實際頁碼由 <see cref="Resolve"/> 展開。
/// </summary>
public sealed class PageRangeSpec
{
    private readonly IReadOnlyList<PageInterval> _intervals;

    private PageRangeSpec(bool isAll, IReadOnlyList<PageInterval> intervals)
    {
        IsAll = isAll;
        _intervals = intervals;
    }

    /// <summary>代表「全部頁面」。</summary>
    public static PageRangeSpec All { get; } = new(true, Array.Empty<PageInterval>());

    public bool IsAll { get; }

    public IReadOnlyList<PageInterval> Intervals => _intervals;

    /// <summary>
    /// 由明確挑選的頁碼建立（縮圖勾選會用到）。會自動排序、去除重複並忽略小於 1 的值。
    /// </summary>
    public static PageRangeSpec FromPages(IEnumerable<int> pages)
    {
        var sorted = new SortedSet<int>(pages.Where(p => p >= 1));
        if (sorted.Count == 0) return new PageRangeSpec(false, Array.Empty<PageInterval>());

        var intervals = new List<PageInterval>();
        var start = -1;
        var previous = -1;

        foreach (var page in sorted)
        {
            if (start < 0) { start = previous = page; continue; }
            if (page == previous + 1) { previous = page; continue; }
            intervals.Add(new PageInterval(start, previous));
            start = previous = page;
        }
        intervals.Add(new PageInterval(start, previous));

        return new PageRangeSpec(false, intervals);
    }

    internal static PageRangeSpec FromIntervals(IReadOnlyList<PageInterval> intervals)
        => intervals.Count == 0 ? All : new PageRangeSpec(false, intervals);

    /// <summary>
    /// 針對指定的總頁數展開為實際頁碼清單（1 起算、已排序、已去除重複、已裁切到有效範圍）。
    /// </summary>
    public IReadOnlyList<int> Resolve(int totalPages)
    {
        if (totalPages <= 0) return Array.Empty<int>();
        if (IsAll) return Enumerable.Range(1, totalPages).ToArray();

        var set = new SortedSet<int>();
        foreach (var interval in _intervals)
        {
            var start = Math.Max(1, interval.Start);
            var end = Math.Min(totalPages, interval.End ?? totalPages);
            for (var p = start; p <= end; p++) set.Add(p);
        }
        return set.ToArray();
    }

    public override string ToString()
        => IsAll ? "全部頁面" : string.Join(",", _intervals.Select(i => i.ToString()));
}

/// <summary>
/// 解析類似 "1-5,8,12-15" 的頁碼字串。
/// 容錯項目：全形逗號／破折號／波浪號、全形數字、多餘空白、開放式區間（"5-"、"-8"）、顛倒區間（"9-3"）。
/// </summary>
public static class PageRangeParser
{
    private const int MaxPageNumber = 100_000;

    public static PageRangeSpec Parse(string? input)
    {
        if (!TryParse(input, out var spec, out var error))
            throw new PageRangeFormatException(error!);
        return spec!;
    }

    public static bool TryParse(string? input, out PageRangeSpec? spec, out string? error)
    {
        spec = null;
        error = null;

        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            spec = PageRangeSpec.All;
            return true;
        }

        var intervals = new List<PageInterval>();
        foreach (var rawToken in normalized.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.Length == 0) continue;

            var dashCount = token.Count(c => c == '-');
            if (dashCount > 1)
            {
                error = $"「{rawToken}」格式不正確，一段範圍只能有一個「-」。";
                return false;
            }

            if (dashCount == 0)
            {
                if (!TryReadPage(token, rawToken, out var page, out error)) return false;
                intervals.Add(new PageInterval(page, page));
                continue;
            }

            var dashIndex = token.IndexOf('-');
            var leftText = token[..dashIndex].Trim();
            var rightText = token[(dashIndex + 1)..].Trim();

            if (leftText.Length == 0 && rightText.Length == 0)
            {
                error = "請至少輸入一個頁碼，例如 1-5,8,12-15。";
                return false;
            }

            int start;
            int? end;

            if (leftText.Length == 0)
            {
                // "-8" 代表第 1 頁到第 8 頁
                if (!TryReadPage(rightText, rawToken, out var e, out error)) return false;
                start = 1;
                end = e;
            }
            else if (rightText.Length == 0)
            {
                // "5-" 代表第 5 頁到最後一頁
                if (!TryReadPage(leftText, rawToken, out var s, out error)) return false;
                start = s;
                end = null;
            }
            else
            {
                if (!TryReadPage(leftText, rawToken, out var s, out error)) return false;
                if (!TryReadPage(rightText, rawToken, out var e, out error)) return false;
                // 顛倒輸入（9-3）自動修正，避免使用者卡在錯誤訊息
                start = Math.Min(s, e);
                end = Math.Max(s, e);
            }

            intervals.Add(new PageInterval(start, end));
        }

        if (intervals.Count == 0)
        {
            error = "請至少輸入一個頁碼，例如 1-5,8,12-15。";
            return false;
        }

        spec = PageRangeSpec.FromIntervals(Merge(intervals));
        return true;
    }

    private static bool TryReadPage(string text, string originalToken, out int page, out string? error)
    {
        page = 0;
        error = null;

        if (text.Length == 0 || !text.All(char.IsAsciiDigit))
        {
            error = $"無法辨識「{originalToken}」，請只輸入數字、「-」與「,」，例如 1-5,8,12-15。";
            return false;
        }

        if (!int.TryParse(text, out page) || page > MaxPageNumber)
        {
            error = $"「{originalToken}」的頁碼太大，請重新確認。";
            return false;
        }

        if (page < 1)
        {
            error = "頁碼必須從 1 開始。";
            return false;
        }

        return true;
    }

    /// <summary>合併重疊或相鄰的區間，讓後續顯示與展開更單純。</summary>
    private static List<PageInterval> Merge(List<PageInterval> intervals)
    {
        // 具有開放結尾的區間，取其中最小的起點即可涵蓋後面所有頁
        var openStart = intervals.Where(i => i.End is null).Select(i => i.Start).DefaultIfEmpty(int.MaxValue).Min();

        var closed = intervals
            .Where(i => i.End is not null)
            .Where(i => i.Start < openStart)
            .Select(i => new PageInterval(i.Start, Math.Min(i.End!.Value, openStart == int.MaxValue ? int.MaxValue : openStart - 1)))
            .Where(i => i.Start <= i.End)
            .OrderBy(i => i.Start)
            .ToList();

        var result = new List<PageInterval>();
        foreach (var interval in closed)
        {
            if (result.Count > 0)
            {
                var last = result[^1];
                if (interval.Start <= last.End!.Value + 1)
                {
                    result[^1] = new PageInterval(last.Start, Math.Max(last.End!.Value, interval.End!.Value));
                    continue;
                }
            }
            result.Add(interval);
        }

        if (openStart != int.MaxValue)
        {
            if (result.Count > 0 && result[^1].End!.Value + 1 >= openStart)
            {
                result[^1] = new PageInterval(result[^1].Start, null);
            }
            else
            {
                result.Add(new PageInterval(openStart, null));
            }
        }

        return result;
    }

    /// <summary>把全形字元、空白與各種破折號正規化成 ASCII。</summary>
    private static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var c = ch;

            // 全形數字 → 半形
            if (c >= '０' && c <= '９') c = (char)(c - '０' + '0');

            c = c switch
            {
                '，' or '、' or '；' or ';' or '　' => ',',
                '－' or '—' or '–' or '─' or 'ー' or '～' or '~' or '至' => '-',
                _ => c
            };

            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }

        return sb.ToString().Trim(',');
    }
}
