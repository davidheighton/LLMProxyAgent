namespace LLMSurvivingProxyAgent.Core;

/// <summary>
/// Weights that convert verified game-state deltas into a scalar learning reward.
/// Asset values are estimates used to keep purchases from looking like pure credit loss;
/// tune them from observed StarDock and port quotes.
/// </summary>
public sealed record GameplayRewardWeights
{
    public static GameplayRewardWeights Default { get; } = new();

    public double CreditsPerRewardUnit { get; init; } = 1_000;
    public double TurnPenalty { get; init; } = 0.02;
    public double NewSectorBonus { get; init; } = 0.05;
    public double ShipLossPenalty { get; init; } = 50;
    public double ScannerMilestoneBonus { get; init; } = 5;

    // Observed: 22 holds cost 14,453 credits at StarDock (reports/event-log-learning.md).
    public long HoldValue { get; init; } = 650;
    public long FighterValue { get; init; } = 100;
    public long ShieldValue { get; init; } = 100;
    public long DensityScannerValue { get; init; } = AutonomousTradePolicy.DensityScannerPurchaseCost;
    public long HoloScannerValue { get; init; } = AutonomousTradePolicy.MinimumHoloScannerUpgradeCredits;
    public long FuelOreValue { get; init; } = 20;
    public long OrganicsValue { get; init; } = 35;
    public long EquipmentValue { get; init; } = 60;

    // Scanner milestones default to 0 because ScannerMilestoneBonus already rewards the purchase step.
    public double ReachedStarDockBonus { get; init; } = 2;
    public double FirstProfitableTradeBonus { get; init; } = 3;
    public double MaxHoldsBonus { get; init; } = 5;
    public double MaxFightersBonus { get; init; } = 5;
    public double MaxShieldsBonus { get; init; } = 5;
    public double FullyUpgradedBonus { get; init; } = 25;

    public double GetMilestoneBonus(EpisodeMilestone milestone) => milestone switch
    {
        EpisodeMilestone.ReachedStarDock => ReachedStarDockBonus,
        EpisodeMilestone.FirstProfitableTrade => FirstProfitableTradeBonus,
        EpisodeMilestone.MaxHolds => MaxHoldsBonus,
        EpisodeMilestone.MaxFighters => MaxFightersBonus,
        EpisodeMilestone.MaxShields => MaxShieldsBonus,
        EpisodeMilestone.FullyUpgraded => FullyUpgradedBonus,
        _ => 0
    };
}

public sealed record GameplayReward(
    bool Verified,
    double Total,
    long? CreditsDelta,
    long? NetWorthDelta,
    int? TurnsUsed,
    bool NewSector,
    bool ScannerAcquired,
    bool ShipLost,
    string? UnverifiedReason)
{
    public static GameplayReward Unverified(string reason) =>
        new(false, 0, null, null, null, false, false, false, reason);
}

public static class GameplayRewardCalculator
{
    /// <summary>
    /// Estimates net worth from the fields present in one snapshot. Returns null without synchronized credits.
    /// </summary>
    public static long? EstimateNetWorth(GameSnapshot? snapshot, GameplayRewardWeights? weights = null)
    {
        if (snapshot?.SynchronizedCredits is not { } credits)
        {
            return null;
        }

        weights ??= GameplayRewardWeights.Default;
        return credits + AssetComponents(snapshot, weights).Sum(component => component ?? 0);
    }

    /// <summary>
    /// Computes the reward for one transition. Only valid, time-ordered synchronized snapshots are
    /// treated as evidence; asset components are compared only when both snapshots report them.
    /// </summary>
    public static GameplayReward Compute(
        GameSnapshot? before,
        GameSnapshot? after,
        bool newSector,
        GameplayRewardWeights? weights = null)
    {
        weights ??= GameplayRewardWeights.Default;
        if (before is null || after is null)
        {
            return GameplayReward.Unverified("A before or after snapshot is missing.");
        }
        if (!GameSnapshotValidation.IsValid(before) || !GameSnapshotValidation.IsValid(after))
        {
            return GameplayReward.Unverified("A snapshot failed synchronized-state validation.");
        }
        if (after.ObservedAt <= before.ObservedAt)
        {
            return GameplayReward.Unverified("The after snapshot is not newer than the before snapshot.");
        }
        if (before.SynchronizedCredits is not { } beforeCredits ||
            after.SynchronizedCredits is not { } afterCredits)
        {
            return GameplayReward.Unverified("Synchronized credits are unavailable.");
        }

        long creditsDelta = afterCredits - beforeCredits;
        long?[] beforeAssets = [.. AssetComponents(before, weights)];
        long?[] afterAssets = [.. AssetComponents(after, weights)];
        long assetDelta = 0;
        for (int index = 0; index < beforeAssets.Length; index++)
        {
            if (beforeAssets[index] is { } beforeAsset && afterAssets[index] is { } afterAsset)
            {
                assetDelta += afterAsset - beforeAsset;
            }
        }

        long netWorthDelta = creditsDelta + assetDelta;
        int? turnsUsed = before.Turns is { } beforeTurns && after.Turns is { } afterTurns
            ? Math.Max(0, beforeTurns - afterTurns)
            : null;
        bool shipLost = IsEscapePod(after) && !IsEscapePod(before);
        bool scannerAcquired =
            before.DensityScannerInstalled is not true && after.DensityScannerInstalled is true ||
            before.HoloScannerInstalled is not true && after.HoloScannerInstalled is true;

        double total =
            netWorthDelta / weights.CreditsPerRewardUnit -
            (turnsUsed ?? 0) * weights.TurnPenalty +
            (newSector ? weights.NewSectorBonus : 0) +
            (scannerAcquired ? weights.ScannerMilestoneBonus : 0) -
            (shipLost ? weights.ShipLossPenalty : 0);

        return new GameplayReward(
            true,
            Math.Round(total, 4),
            creditsDelta,
            netWorthDelta,
            turnsUsed,
            newSector,
            scannerAcquired,
            shipLost,
            null);
    }

    public static bool IsEscapePod(GameSnapshot? snapshot) =>
        snapshot?.Ship?.ShipType.Contains("escape pod", StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<long?> AssetComponents(GameSnapshot snapshot, GameplayRewardWeights weights)
    {
        yield return snapshot.HoldsTotal * weights.HoldValue;
        yield return snapshot.Ship?.Fighters * weights.FighterValue;
        yield return snapshot.Shields * weights.ShieldValue;
        yield return snapshot.DensityScannerInstalled is { } density
            ? density ? weights.DensityScannerValue : 0
            : null;
        yield return snapshot.HoloScannerInstalled is { } holo
            ? holo ? weights.HoloScannerValue : 0
            : null;
        yield return snapshot.FuelOre * weights.FuelOreValue;
        yield return snapshot.Organics * weights.OrganicsValue;
        yield return snapshot.Equipment * weights.EquipmentValue;
    }
}
