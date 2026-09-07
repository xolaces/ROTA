namespace ROTA.Application.Interfaces;

/// <summary>
/// What a beta reset would do, or did. Returned by both the dry run and the real run so the
/// operator reads the same shape either way.
/// </summary>
public sealed class BetaResetReport
{
    /// <summary>True when nothing was written — the operator asked for a preview.</summary>
    public bool DryRun { get; set; }

    /// <summary>Accounts whose progress was (or would be) reset, keeping their login.</summary>
    public int PlayersReset { get; set; }

    /// <summary>Accounts deleted outright. Zero unless the operator opted into a purge.</summary>
    public int PlayersPurged { get; set; }

    /// <summary>Unredeemed beta keys removed. Redeemed keys are never touched.</summary>
    public int UnredeemedKeysDeleted { get; set; }

    /// <summary>Redeemed keys left in place, so the record of who got in survives the wipe.</summary>
    public int RedeemedKeysKept { get; set; }

    /// <summary>Rows deleted per table, highest first. Empty tables are omitted.</summary>
    public Dictionary<string, long> RowsDeletedByTable { get; set; } = new();

    /// <summary>Tables deliberately left alone, and why.</summary>
    public Dictionary<string, string> TablesKept { get; set; } = new();

    /// <summary>
    /// Player-owned tables the classifier did not recognise. Non-empty means the reset REFUSED to
    /// run: an unclassified table is one nobody has decided about, and guessing on a destructive
    /// operation is how a wipe leaves a player owning chapter-6 progress on a level-1 account.
    /// </summary>
    public List<string> UnclassifiedTables { get; set; } = new();

    /// <summary>Total rows across every wiped table.</summary>
    public long TotalRowsDeleted => RowsDeletedByTable.Values.Sum();
}

/// <summary>
/// Between-wave beta maintenance: clears the world, keeps the logins.
/// </summary>
/// <remarks>
/// This is a destructive maintenance operation and is deliberately not reachable over HTTP. It runs
/// only from the <c>beta-reset</c> admin CLI command, which requires an explicit confirmation
/// phrase, and it always runs inside one transaction so a failure halfway leaves nothing behind.
/// </remarks>
public interface IBetaResetService
{
    /// <summary>
    /// Resets the beta world.
    /// </summary>
    /// <param name="dryRun">
    /// When true, counts everything and writes nothing. Always run this first — the report it
    /// returns is exactly what the real run will do.
    /// </param>
    /// <param name="purgeAccounts">
    /// When true, DELETES player accounts outright instead of resetting them, so nobody keeps a
    /// login. Off by default: the normal beta reset keeps logins so returning testers do not have
    /// to re-register.
    /// </param>
    /// <param name="keepUsernames">
    /// Accounts exempted entirely — neither reset nor purged. Case-insensitive. The seeded Owner
    /// belongs here on a live run, or the operator locks themselves out of their own admin account
    /// mid-wipe.
    /// </param>
    Task<BetaResetReport> RunAsync(
        bool dryRun,
        bool purgeAccounts = false,
        IReadOnlyCollection<string>? keepUsernames = null,
        CancellationToken ct = default);
}
