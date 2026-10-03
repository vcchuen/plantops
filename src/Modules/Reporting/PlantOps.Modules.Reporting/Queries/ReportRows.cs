namespace PlantOps.Modules.Reporting.Queries;

// The API's row shapes. They are the contract with the web client: names and order are not an implementation detail.

internal sealed record MttrRow(string Key, string Label, int WorkOrders, double MeanRepairMinutes);

internal sealed record SlaRow(string Key, string Label, int Completed, int MetSla, double CompliancePercent);

internal sealed record DowntimeRow(string Key, string Label, int Events, int DowntimeMinutes);

internal sealed record ReportResponse<TRow>(DateOnly From, DateOnly To, string GroupBy, IReadOnlyList<TRow> Rows);
