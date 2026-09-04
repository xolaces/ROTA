namespace ROTA.Shared;

/// <summary>
/// One place to turn a 1-based page number into a row offset.
///
/// Every paged query wrote <c>(page - 1) * pageSize</c> in int arithmetic. That silently overflows:
/// at PageSize 200 a request for page 10,737,420 wraps the product negative, and PostgreSQL answers
/// "OFFSET must not be negative" — a 500 that any authenticated caller could produce at will on
/// /api/leaderboards, /api/guilds and /api/admin/emails. The multiply happens in <see cref="long"/>
/// here so it cannot wrap, and a page past the end simply returns nothing, which is the honest
/// answer for a page past the end.
/// </summary>
public static class Paging
{
    /// <summary>
    /// Row offset for a 1-based <paramref name="page"/>. Never negative, never overflows, and
    /// saturates at <see cref="int.MaxValue"/> so it stays inside the int the query providers take.
    /// </summary>
    public static int Offset(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1) return 0;
        long offset = (long)(page - 1) * pageSize;
        return offset > int.MaxValue ? int.MaxValue : (int)offset;
    }
}
