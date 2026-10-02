namespace PlantOps.Modules.Assets.Domain;

internal readonly record struct AssetId(Guid Value)
{
    // Version 7 GUIDs are time-ordered. Note SQL Server sorts uniqueidentifier by its last bytes, so this
    // does not give sequential clustered-index inserts there; it still keeps ids roughly creation-ordered.
    public static AssetId New() => new(Guid.CreateVersion7());
}
