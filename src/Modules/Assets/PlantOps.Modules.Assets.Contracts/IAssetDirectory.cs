namespace PlantOps.Modules.Assets.Contracts;

/// <summary>What other modules may know about an asset. Read-only on purpose: Assets owns the data.</summary>
public interface IAssetDirectory
{
    Task<AssetSummary?> FindAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record AssetSummary(Guid Id, string Tag, string Name, Guid LineId, string LineName, bool IsInService);
