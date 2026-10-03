using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PlantOps.Modules.WorkOrders.Contracts;

namespace PlantOps.Functions;

// Timer triggers take a blob lease (the storage account in AzureWebJobsStorage), so with several instances only one
// runs each occurrence. The runners are idempotent anyway, so a rare double fire is harmless (ADR-0010).
public sealed class SlaEscalationTimer(ISlaEscalationRunner runner, ILogger<SlaEscalationTimer> logger)
{
    [Function("SlaEscalationTimer")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var escalated = await runner.RunAsync(cancellationToken);
        logger.LogInformation("SLA escalation run finished: {Escalated} escalated", escalated);
    }
}

public sealed class PreventiveMaintenanceTimer(IPreventiveMaintenanceRunner runner, ILogger<PreventiveMaintenanceTimer> logger)
{
    // NCRONTAB is {second} {minute} {hour} {day} {month} {day-of-week}. 22:00 UTC = 06:00 Asia/Kuala_Lumpur (UTC+8,
    // no DST), the start of the factory's day. A cron expression is evaluated in the host's time zone, which is UTC on
    // Linux. WEBSITE_TIME_ZONE would change that, but it is not supported on Linux Consumption (and not on Flex
    // Consumption either), so the schedule is written in UTC instead of relying on it. Which DAY a run counts as
    // "today" does not come from this cron at all: the runner asks FactoryClock (Factory:TimeZone).
    [Function("PreventiveMaintenanceTimer")]
    public async Task Run([TimerTrigger("0 0 22 * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var generated = await runner.RunAsync(cancellationToken);
        logger.LogInformation("Preventive maintenance run finished: {Generated} work order(s) generated", generated);
    }
}
