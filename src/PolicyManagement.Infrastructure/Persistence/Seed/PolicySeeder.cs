using PolicyManagement.Domain.Constants;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Infrastructure.Persistence.Seed;

/// <summary>
/// Generates a realistic set of 210 seed <see cref="Policy"/> records covering all eight
/// APAC regions, all four statuses, all four lines of business, and all six currencies.
/// Used by <see cref="PolicyDbContext.SeedAsync"/> on first startup in the development
/// environment when the <c>Policies</c> table is empty.
/// </summary>
public static class PolicySeeder
{
    private static readonly string[] Regions =
    [
        Domain.Constants.Regions.Singapore,
        Domain.Constants.Regions.HongKong,
        Domain.Constants.Regions.Australia,
        Domain.Constants.Regions.Japan,
        Domain.Constants.Regions.Thailand,
        Domain.Constants.Regions.Indonesia,
        Domain.Constants.Regions.Malaysia,
        Domain.Constants.Regions.Philippines,
    ];

    private static readonly string[] CurrencyCodes =
    [
        Currencies.USD,
        Currencies.SGD,
        Currencies.HKD,
        Currencies.AUD,
        Currencies.JPY,
        Currencies.THB,
    ];

    private static readonly PolicyStatus[] Statuses =
    [
        PolicyStatus.Active,
        PolicyStatus.Expired,
        PolicyStatus.Pending,
        PolicyStatus.Cancelled,
    ];

    private static readonly LineOfBusiness[] LinesOfBusiness =
    [
        LineOfBusiness.Property,
        LineOfBusiness.Casualty,
        LineOfBusiness.AH,
        LineOfBusiness.Marine,
    ];

    private static readonly string[] Underwriters =
    [
        "Alice Chen",
        "Bob Tanaka",
        "Clara Singh",
        "David Lim",
        "Emma Walsh",
        "Frank Ho",
        "Grace Kim",
        "Henry Patel",
        "Isabella Cruz",
        "James Wong",
    ];

    private static readonly string[] PolicyholderPrefixes =
    [
        "Pacific", "Asian", "Global", "Regional", "Premier",
        "United", "Alliance", "Summit", "Apex", "Horizon",
        "Meridian", "Nexus", "Pinnacle", "Sterling", "Titan",
    ];

    private static readonly string[] PolicyholderSuffixes =
    [
        "Holdings Ltd", "Group Pte Ltd", "Corporation", "Industries Bhd",
        "Enterprises Inc", "Partners Co", "Ventures Ltd", "Capital Group",
        "Solutions Corp", "Resources Ltd", "Logistics Pte", "Maritime Sdn Bhd",
        "Properties Co", "Technologies Ltd", "Financial Services",
    ];

    /// <summary>
    /// Generates 210 deterministic seed policies.
    /// The seed is deterministic — each run produces the same IDs and values,
    /// making it idempotent when applied to an empty table.
    /// </summary>
    public static IReadOnlyList<Policy> Generate()
    {
        var policies = new List<Policy>(210);
        var baseDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 210 records: 7 per combination of region (8) × status (4) × LOB (4)
        // Distributed across all combinations; sequential seed index drives determinism.
        int index = 0;
        for (int regionIdx = 0; regionIdx < Regions.Length; regionIdx++)
        {
            for (int statusIdx = 0; statusIdx < Statuses.Length; statusIdx++)
            {
                for (int lobIdx = 0; lobIdx < LinesOfBusiness.Length; lobIdx++)
                {
                    // Produce ~1 record per combination (8×4×4 = 128 combinations)
                    // plus extra iterations to reach 210+
                    int count = (index % 5 == 0) ? 2 : 1;
                    for (int c = 0; c < count && policies.Count < 210; c++)
                    {
                        index++;
                        var policy = BuildPolicy(
                            index: index,
                            region: Regions[regionIdx],
                            status: Statuses[statusIdx],
                            lob: LinesOfBusiness[lobIdx],
                            currency: CurrencyCodes[index % CurrencyCodes.Length],
                            underwriter: Underwriters[index % Underwriters.Length],
                            baseDate: baseDate,
                            today: today);
                        policies.Add(policy);
                    }
                }
            }
        }

        // Fill to exactly 210 if not already reached
        while (policies.Count < 210)
        {
            index++;
            var policy = BuildPolicy(
                index: index,
                region: Regions[index % Regions.Length],
                status: Statuses[index % Statuses.Length],
                lob: LinesOfBusiness[index % LinesOfBusiness.Length],
                currency: CurrencyCodes[index % CurrencyCodes.Length],
                underwriter: Underwriters[index % Underwriters.Length],
                baseDate: baseDate,
                today: today);
            policies.Add(policy);
        }

        return policies;
    }

    private static Policy BuildPolicy(
        int index,
        string region,
        PolicyStatus status,
        LineOfBusiness lob,
        string currency,
        string underwriter,
        DateTimeOffset baseDate,
        DateOnly today)
    {
        // Deterministic GUID from index — same every run
        var id = new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var policyNumber = $"POL-{index:D6}";

        var prefixIdx = index % PolicyholderPrefixes.Length;
        var suffixIdx = (index / PolicyholderPrefixes.Length) % PolicyholderSuffixes.Length;
        var holderName = $"{PolicyholderPrefixes[prefixIdx]} {PolicyholderSuffixes[suffixIdx]}";

        // Spread premium across a realistic range: 1,000 – 5,000,000
        var premium = Math.Round(1000m + (index * 23456.78m % 4999000m), 2);

        // Date spread: effective dates within last 3 years; expiry based on status
        var effectiveDate = today.AddDays(-(index % 1095)); // up to 3 years ago
        DateOnly expiryDate;
        bool flagged = index % 7 == 0;

        switch (status)
        {
            case PolicyStatus.Active:
                // Active policies expire in the future (mix: some expiring soon)
                expiryDate = index % 10 == 0
                    ? today.AddDays(15)   // expiring within 30 days (drives expiringSoonCount)
                    : today.AddDays(180 + index % 365);
                break;
            case PolicyStatus.Expired:
                // Expired policies have past expiry dates
                expiryDate = today.AddDays(-(1 + index % 365));
                break;
            case PolicyStatus.Pending:
                // Pending policies have future effective and expiry dates
                effectiveDate = today.AddDays(30 + index % 180);
                expiryDate = effectiveDate.AddDays(365);
                break;
            case PolicyStatus.Cancelled:
                // Cancelled policies may have any date range
                expiryDate = today.AddDays(-(index % 730));
                if (expiryDate <= effectiveDate)
                    expiryDate = effectiveDate.AddDays(1);
                break;
            default:
                expiryDate = effectiveDate.AddDays(365);
                break;
        }

        // Ensure expiryDate > effectiveDate (domain invariant)
        if (expiryDate <= effectiveDate)
            expiryDate = effectiveDate.AddDays(1);

        var createdAt = baseDate.AddDays(index);

        var policy = Policy.Create(
            id: id,
            policyNumber: policyNumber,
            policyholderName: holderName,
            lineOfBusiness: lob,
            status: status,
            premiumAmount: premium,
            currency: currency,
            effectiveDate: effectiveDate,
            expiryDate: expiryDate,
            region: region,
            underwriter: underwriter,
            now: createdAt);

        if (flagged)
            policy.Flag(createdAt.AddDays(1));

        return policy;
    }
}
