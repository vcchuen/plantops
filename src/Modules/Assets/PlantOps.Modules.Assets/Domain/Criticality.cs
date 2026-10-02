namespace PlantOps.Modules.Assets.Domain;

/// <summary>ABC criticality classification; drives SLA targets later.</summary>
internal enum Criticality
{
    /// <summary>Failure stops a production line.</summary>
    A,

    /// <summary>Failure degrades a line (reduced rate or quality).</summary>
    B,

    /// <summary>A workaround exists, so failure does not affect output.</summary>
    C,
}
