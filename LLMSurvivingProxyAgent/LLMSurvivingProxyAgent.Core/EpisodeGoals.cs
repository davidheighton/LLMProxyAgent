namespace LLMSurvivingProxyAgent.Core;

public enum EpisodeGoal
{
    None,
    FullyUpgradedShip
}

public enum EpisodeMilestone
{
    ReachedStarDock,
    DensityScanner,
    FirstProfitableTrade,
    HoloScanner,
    MaxHolds,
    MaxFighters,
    MaxShields,
    FullyUpgraded
}

public enum EpisodeStatus
{
    Running,
    Completed,
    ShipLost,
    PausedOutOfTurns,
    TimedOut,
    Stopped,
    Error
}

/// <summary>Maximum loadout for one ship class, from a universe ship catalog.</summary>
public sealed record ShipCapacity(
    string ShipClass,
    int MaxHolds,
    int MaxFighters,
    int MaxShields,
    string Source);

public interface IShipCapacityProvider
{
    ShipCapacity? GetCapacity(string? shipClass);
}

/// <summary>
/// Reads a Mombot ship catalog (<c>ships.cfg</c>): nine numeric fields followed by the ship class name.
/// Field order: shields, defensive odds, offensive odds, cost, max holds, max fighters, initial holds,
/// turns per warp, defender bonus.
/// </summary>
public sealed class ShipCatalogCapacityProvider(Func<string?> getCatalogPath) : IShipCapacityProvider
{
    private readonly object _sync = new();
    private string? _loadedPath;
    private DateTime _loadedWriteTime;
    private Dictionary<string, ShipCapacity> _entries = new(StringComparer.OrdinalIgnoreCase);

    public ShipCapacity? GetCapacity(string? shipClass)
    {
        if (string.IsNullOrWhiteSpace(shipClass))
        {
            return null;
        }

        Dictionary<string, ShipCapacity> entries = LoadEntries();
        return entries.TryGetValue(shipClass.Trim(), out ShipCapacity? capacity) ? capacity : null;
    }

    public static IReadOnlyList<ShipCapacity> Parse(IEnumerable<string> lines, string source)
    {
        var capacities = new List<ShipCapacity>();
        foreach (string line in lines)
        {
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 10 ||
                !int.TryParse(tokens[0], out int shields) ||
                !int.TryParse(tokens[4], out int holds) ||
                !int.TryParse(tokens[5], out int fighters) ||
                shields < 0 || holds <= 0 || fighters < 0)
            {
                continue;
            }

            capacities.Add(new ShipCapacity(string.Join(' ', tokens.Skip(9)), holds, fighters, shields, source));
        }
        return capacities;
    }

    private Dictionary<string, ShipCapacity> LoadEntries()
    {
        string? path = getCatalogPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new Dictionary<string, ShipCapacity>(StringComparer.OrdinalIgnoreCase);
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(path);
        lock (_sync)
        {
            if (!string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase) || writeTime != _loadedWriteTime)
            {
                _entries = Parse(File.ReadLines(path), $"ship catalog {Path.GetFileName(path)}")
                    .GroupBy(entry => entry.ShipClass, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                _loadedPath = path;
                _loadedWriteTime = writeTime;
            }
            return _entries;
        }
    }
}

public sealed record EpisodeMilestoneObservation(
    EpisodeMilestone Milestone,
    string Evidence);

public sealed record EpisodeGoalContext(
    GameSnapshot Snapshot,
    int? StarDockSector,
    ShipCapacity? Capacity,
    AutonomousRefurbQuote? RefurbQuote,
    TrajectoryStep? LatestClosedStep);

public static class EpisodeGoalEvaluator
{
    public static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaximumQuoteAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Returns milestones newly evidenced by a fresh, valid synchronized snapshot. Previously reached
    /// milestones are never repeated. "Max" milestones need a known ship capacity, or a fresh Refurb quote
    /// offering zero more units while credits exceed the unit cost (so credits were not the limit).
    /// </summary>
    public static IReadOnlyList<EpisodeMilestoneObservation> Evaluate(
        EpisodeGoalContext context,
        IReadOnlySet<EpisodeMilestone> alreadyReached,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(alreadyReached);
        GameSnapshot snapshot = context.Snapshot;
        if (!GameSnapshotValidation.IsValid(snapshot) ||
            snapshot.ObservedAt > now ||
            now - snapshot.ObservedAt > MaximumSnapshotAge ||
            GameplayRewardCalculator.IsEscapePod(snapshot))
        {
            return [];
        }

        var reached = new List<EpisodeMilestoneObservation>();
        void Add(EpisodeMilestone milestone, string evidence)
        {
            if (!alreadyReached.Contains(milestone) && reached.All(item => item.Milestone != milestone))
            {
                reached.Add(new EpisodeMilestoneObservation(milestone, evidence));
            }
        }

        if (context.StarDockSector is > 0 and var dock && snapshot.CurrentSector == dock)
        {
            Add(EpisodeMilestone.ReachedStarDock, $"Synchronized sector {dock} is StarDock.");
        }
        if (snapshot.DensityScannerInstalled is true || snapshot.HoloScannerInstalled is true)
        {
            Add(EpisodeMilestone.DensityScanner, "Synchronized status reports a Density-capable scanner.");
        }
        if (snapshot.HoloScannerInstalled is true)
        {
            Add(EpisodeMilestone.HoloScanner, "Synchronized status reports a Holo Scanner.");
        }
        if (context.LatestClosedStep is
            {
                Action: nameof(AutonomousTestAction.TradeAtCurrentPort),
                Reward: { Verified: true, CreditsDelta: > 0 } reward
            })
        {
            Add(EpisodeMilestone.FirstProfitableTrade, $"Verified port trade gained {reward.CreditsDelta:N0} credits.");
        }

        bool holdsMaxed = IsMaxed(
            snapshot.HoldsTotal, context.Capacity?.MaxHolds,
            context.RefurbQuote, quote => (quote.CargoHoldsAvailable, quote.CargoHoldUnitCost), snapshot, now);
        bool fightersMaxed = IsMaxed(
            snapshot.Ship?.Fighters, context.Capacity?.MaxFighters,
            context.RefurbQuote, quote => (quote.FightersAvailable, quote.FighterUnitCost), snapshot, now);
        bool shieldsMaxed = IsMaxed(
            snapshot.Shields, context.Capacity?.MaxShields,
            context.RefurbQuote, quote => (quote.ShieldsAvailable, quote.ShieldUnitCost), snapshot, now);
        if (holdsMaxed)
        {
            Add(EpisodeMilestone.MaxHolds, $"{snapshot.HoldsTotal} holds is the ship maximum.");
        }
        if (fightersMaxed)
        {
            Add(EpisodeMilestone.MaxFighters, $"{snapshot.Ship?.Fighters} fighters is the ship maximum.");
        }
        if (shieldsMaxed)
        {
            Add(EpisodeMilestone.MaxShields, $"{snapshot.Shields} shields is the ship maximum.");
        }
        if (snapshot.HoloScannerInstalled is true && holdsMaxed && fightersMaxed && shieldsMaxed)
        {
            Add(EpisodeMilestone.FullyUpgraded, "Holo Scanner plus maximum holds, fighters, and shields verified together.");
        }

        return reached;
    }

    private static bool IsMaxed(
        int? current,
        int? maximum,
        AutonomousRefurbQuote? quote,
        Func<AutonomousRefurbQuote, (int Available, int UnitCost)> select,
        GameSnapshot snapshot,
        DateTimeOffset now)
    {
        if (current is not { } value)
        {
            return false;
        }
        if (maximum is > 0 and var max)
        {
            return value >= max;
        }
        if (quote is null || now - quote.ObservedAt > MaximumQuoteAge || quote.Sector != snapshot.CurrentSector)
        {
            return false;
        }

        (int available, int unitCost) = select(quote);
        return available == 0 && unitCost > 0 && snapshot.SynchronizedCredits is { } credits && credits >= unitCost;
    }
}
