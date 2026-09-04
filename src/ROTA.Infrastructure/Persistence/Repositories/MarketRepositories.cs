using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Repositories;

/// <summary>
/// System 27. The three state transitions out of Active (sell / cancel / expire) are raw conditional
/// UPDATEs rather than EF saves, for the reason BetaKeyRepository.TryRedeemAsync gives: the WHERE
/// clause IS the race guard. A read-then-write through the change tracker would let two buyers both
/// observe Active and both proceed, and the loser would be charged for goods the winner already has.
/// </summary>
public sealed class MarketListingRepository : IMarketListingRepository
{
    private readonly RotaDbContext _db;

    public MarketListingRepository(RotaDbContext db) => _db = db;

    public async Task CreateAsync(MarketListing listing, CancellationToken ct = default)
    {
        _db.MarketListings.Add(listing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(MarketListing listing, CancellationToken ct = default)
    {
        _db.MarketListings.Update(listing);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<MarketListing?> FindByIdAsync(Guid listingId, CancellationToken ct = default)
        => await _db.MarketListings.FirstOrDefaultAsync(l => l.Id == listingId, ct);

    public async Task<IReadOnlyList<MarketListing>> BrowseAsync(
        string? kind, string? definitionId, int page, int pageSize, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var q = _db.MarketListings.AsNoTracking()
            .Where(l => !l.IsDeleted
                     && l.Status == MarketListingStatus.Active
                     && l.ExpiresAt > now);

        if (!string.IsNullOrWhiteSpace(kind)
            && Enum.TryParse<MarketItemKind>(kind, ignoreCase: true, out var parsed))
            q = q.Where(l => l.Kind == parsed);

        if (!string.IsNullOrWhiteSpace(definitionId))
            q = q.Where(l => l.DefinitionId == definitionId);

        return await q
            .OrderBy(l => l.UnitPrice)      // cheapest first: the board's job is to find a price
            .ThenBy(l => l.ListedAt)
            .ThenBy(l => l.Id)              // terminal tiebreak, so paging cannot repeat or skip a row
            .Skip(ROTA.Shared.Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MarketListing>> GetForSellerAsync(
        Guid sellerId, CancellationToken ct = default)
        => await _db.MarketListings.AsNoTracking()
            .Where(l => l.SellerId == sellerId && !l.IsDeleted)
            .OrderByDescending(l => l.ListedAt)
            .Take(200)
            .ToListAsync(ct);

    public async Task<int> CountActiveForSellerAsync(Guid sellerId, CancellationToken ct = default)
        => await _db.MarketListings.AsNoTracking()
            .CountAsync(l => l.SellerId == sellerId
                          && !l.IsDeleted
                          && l.Status == MarketListingStatus.Active, ct);

    public async Task<IReadOnlyList<MarketListing>> GetExpiredActiveAsync(
        DateTimeOffset asOf, int max, CancellationToken ct = default)
        => await _db.MarketListings.AsNoTracking()
            .Where(l => !l.IsDeleted
                     && l.Status == MarketListingStatus.Active
                     && l.ExpiresAt <= asOf)
            .OrderBy(l => l.ExpiresAt)
            .Take(Math.Clamp(max, 1, 500))
            .ToListAsync(ct);

    public Task<bool> TryClaimForSaleAsync(
        Guid listingId, Guid buyerId, long salePrice, long saleFee, CancellationToken ct = default)
        => ConditionalUpdateAsync(
            """
            UPDATE market_listings
               SET status        = 1,
                   buyer_id      = @buyer,
                   sold_at       = now(),
                   sale_price    = @price,
                   sale_fee_paid = @fee,
                   updated_at    = now()
             WHERE id         = @id
               AND status     = 0
               AND is_deleted = false
               AND expires_at > now()
               AND seller_id <> @buyer
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("id",    NpgsqlDbType.Uuid,   listingId);
                cmd.Parameters.AddWithValue("buyer", NpgsqlDbType.Uuid,   buyerId);
                cmd.Parameters.AddWithValue("price", NpgsqlDbType.Bigint, salePrice);
                cmd.Parameters.AddWithValue("fee",   NpgsqlDbType.Bigint, saleFee);
            }, ct);

    public Task<bool> TryCancelAsync(Guid listingId, Guid sellerId, CancellationToken ct = default)
        => ConditionalUpdateAsync(
            """
            UPDATE market_listings
               SET status = 2, updated_at = now()
             WHERE id         = @id
               AND seller_id  = @seller
               AND status     = 0
               AND is_deleted = false
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("id",     NpgsqlDbType.Uuid, listingId);
                cmd.Parameters.AddWithValue("seller", NpgsqlDbType.Uuid, sellerId);
            }, ct);

    public Task<bool> TryExpireAsync(Guid listingId, CancellationToken ct = default)
        => ConditionalUpdateAsync(
            """
            UPDATE market_listings
               SET status = 3, updated_at = now()
             WHERE id         = @id
               AND status     = 0
               AND is_deleted = false
               AND expires_at <= now()
            """,
            cmd => cmd.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, listingId), ct);

    // One statement, one row, one winner. Runs on the ambient transaction when there is one, so the
    // claim rolls back with everything else if a later step in the sale fails.
    private async Task<bool> ConditionalUpdateAsync(
        string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
        var ntx = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();

        await using var cmd = new NpgsqlCommand(sql, conn, ntx);
        bind(cmd);
        int rows = await cmd.ExecuteNonQueryAsync(ct);

        if (rows == 1)
        {
            // The row moved underneath EF. Drop any stale tracked copy so a later read in this same
            // request sees the committed status rather than Active.
            var tracked = _db.ChangeTracker.Entries<MarketListing>().ToList();
            foreach (var e in tracked) e.State = EntityState.Detached;
        }
        return rows == 1;
    }
}

/// <summary>
/// The trade ledger. Append-only by construction: this type exposes no update and no delete, and the
/// two reads are aggregates that feed the daily caps.
/// </summary>
public sealed class MarketTransactionRepository : IMarketTransactionRepository
{
    private readonly RotaDbContext _db;

    public MarketTransactionRepository(RotaDbContext db) => _db = db;

    public async Task AppendAsync(MarketTransaction transaction, CancellationToken ct = default)
    {
        _db.MarketTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<long> SumProceedsSinceAsync(
        Guid sellerId, DateTimeOffset since, CancellationToken ct = default)
        => await _db.MarketTransactions.AsNoTracking()
            .Where(t => t.SellerId == sellerId && t.OccurredAt >= since && !t.IsDeleted)
            .SumAsync(t => (long?)t.SellerProceeds, ct) ?? 0L;

    public async Task<long> SumSpendSinceAsync(
        Guid buyerId, DateTimeOffset since, CancellationToken ct = default)
        => await _db.MarketTransactions.AsNoTracking()
            .Where(t => t.BuyerId == buyerId && t.OccurredAt >= since && !t.IsDeleted)
            .SumAsync(t => (long?)t.TotalPrice, ct) ?? 0L;
}
