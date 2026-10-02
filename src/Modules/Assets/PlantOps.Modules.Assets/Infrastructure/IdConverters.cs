using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PlantOps.Modules.Assets.Domain;

namespace PlantOps.Modules.Assets.Infrastructure;

internal sealed class AssetIdConverter()
    : ValueConverter<AssetId, Guid>(id => id.Value, value => new AssetId(value));

internal sealed class ProductionLineIdConverter()
    : ValueConverter<ProductionLineId, Guid>(id => id.Value, value => new ProductionLineId(value));
