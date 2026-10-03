using Microsoft.EntityFrameworkCore;
using PlantOps.Modules.Assets.Contracts;
using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Infrastructure;

internal sealed class AssetDirectory(AssetsDbContext db) : IAssetDirectory
{
    public Task<AssetSummary?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var assetId = new AssetId(id);
        return db.Assets
            .AsNoTracking()
            .Where(a => a.Id == assetId)
            .Join(db.ProductionLines, a => a.LineId, l => l.Id, (a, l) => new AssetSummary(
                a.Id.Value,
                EF.Property<string>(a, Asset.TagField),
                a.Name,
                l.Id.Value,
                l.Name,
                a.Status == AssetStatus.InService))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
