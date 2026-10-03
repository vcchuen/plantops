# Azure cost (list price floor, not a measurement)

> **Status:** nothing has been deployed, so there is **no actual spend** to report. Everything below is the public list price returned by the Azure Retail Prices API on **2026-10-03**, multiplied by a stated assumption. Where a meter or a free grant could not be verified, this page says so.

## How the prices were obtained

`deploy/azure/cost/prices.sh` calls the read-only, unauthenticated API <https://prices.azure.com/api/retail/prices> (docs: <https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices>) with an OData `$filter`. The exact filter for each row is in the table. Re-run it any time: `deploy/azure/cost/prices.sh [region]`.

"Tier starts at" is the API's `tierMinimumUnits`: a row only applies from that cumulative usage upward within the month (a row at `from 0` and price 0 followed by a row at a higher tier is a free first slice).

## Unit prices (southeastasia, USD, list)

Region: southeastasia. Currency: USD. Prices are list (retail) prices, not what a particular agreement pays.

| Date | Item | API filter (plus region and priceType=Consumption) | Meter (product / sku / meter) | Tier starts at | Unit price | Unit |
|---|---|---|---|---|---|---|
| 2026-10-03 | App Service Linux B1 | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Azure App Service' and productName eq 'Azure App Service Basic Plan - Linux' and skuName eq 'B1'` | Azure App Service Basic Plan - Linux / B1 / B1 | from 0 | 0.018 USD | 1 Hour |
| 2026-10-03 | App Service Linux F1 | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Azure App Service' and productName eq 'Azure App Service Free Plan - Linux' and skuName eq 'F1'` | Azure App Service Free Plan - Linux / F1 / F1 App | from 0 | 0 USD | 1 Hour |
| 2026-10-03 | Service Bus Standard base unit | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Base Unit'` | Service Bus / Standard / Standard Base Unit | from 0 | 0.013441 USD | 1/Hour |
| 2026-10-03 | Service Bus Standard base unit | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Base Unit'` | Service Bus / Standard / Standard Base Unit | from 0 | 10 USD | 1/Month |
| 2026-10-03 | Service Bus Standard operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Messaging Operations'` | Service Bus / Standard / Standard Messaging Operations | from 0 | 0 USD | 1M |
| 2026-10-03 | Service Bus Standard operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Messaging Operations'` | Service Bus / Standard / Standard Messaging Operations | from 13 | 0.8 USD | 1M |
| 2026-10-03 | Service Bus Standard operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Messaging Operations'` | Service Bus / Standard / Standard Messaging Operations | from 100 | 0.5 USD | 1M |
| 2026-10-03 | Service Bus Standard operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Messaging Operations'` | Service Bus / Standard / Standard Messaging Operations | from 2500 | 0.2 USD | 1M |
| 2026-10-03 | Key Vault Standard operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Key Vault' and productName eq 'Key Vault' and skuName eq 'Standard' and meterName eq 'Operations'` | Key Vault / Standard / Operations | from 0 | 0.03 USD | 10K |
| 2026-10-03 | Storage (StorageV2, Hot LRS) data stored | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Data Stored'` | General Block Blob v2 / Hot LRS / Hot LRS Data Stored | from 0 | 0.02 USD | 1 GB/Month |
| 2026-10-03 | Storage (StorageV2, Hot LRS) data stored | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Data Stored'` | General Block Blob v2 / Hot LRS / Hot LRS Data Stored | from 51200 | 0.0192 USD | 1 GB/Month |
| 2026-10-03 | Storage (StorageV2, Hot LRS) data stored | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Data Stored'` | General Block Blob v2 / Hot LRS / Hot LRS Data Stored | from 512000 | 0.0184 USD | 1 GB/Month |
| 2026-10-03 | Storage (StorageV2, Hot LRS) write operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Write Operations'` | General Block Blob v2 / Hot LRS / Hot LRS Write Operations | from 0 | 0.05 USD | 10K |
| 2026-10-03 | Storage (StorageV2, Hot LRS) read operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot Read Operations'` | General Block Blob v2 / Hot LRS / Hot Read Operations | from 0 | 0.004 USD | 10K |
| 2026-10-03 | Storage (StorageV2, Hot LRS) other operations | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'All Other Operations'` | General Block Blob v2 / Hot LRS / All Other Operations | from 0 | 0.004 USD | 10K |
| 2026-10-03 | Log Analytics ingestion (Analytics Logs) | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Log Analytics' and skuName eq 'Analytics Logs' and meterName eq 'Analytics Logs Data Ingestion'` | Log Analytics / Analytics Logs / Analytics Logs Data Ingestion | from 0 | 0 USD | 1 GB |
| 2026-10-03 | Log Analytics ingestion (Analytics Logs) | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Log Analytics' and skuName eq 'Analytics Logs' and meterName eq 'Analytics Logs Data Ingestion'` | Log Analytics / Analytics Logs / Analytics Logs Data Ingestion | from 5 | 2.99 USD | 1 GB |
| 2026-10-03 | Azure SQL serverless General Purpose Gen5 vCore (beyond the free grant) | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'SQL Database' and productName eq 'SQL Database General Purpose - Serverless - Compute Gen5' and meterName eq 'vCore'` | SQL Database General Purpose - Serverless - Compute Gen5 / 1 vCore / vCore | from 0 | 0.620892 USD (= 0.00017247 per vCore-second) | 1 Hour |
| 2026-10-03 | Functions Flex Consumption on-demand execution time | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Functions' and productName eq 'Flex Consumption' and skuName eq 'On Demand' and meterName eq 'On Demand Execution Time'` | Flex Consumption / On Demand / On Demand Execution Time | from 0 | 0 USD | 1 GB Second |
| 2026-10-03 | Functions Flex Consumption on-demand execution time | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Functions' and productName eq 'Flex Consumption' and skuName eq 'On Demand' and meterName eq 'On Demand Execution Time'` | Flex Consumption / On Demand / On Demand Execution Time | from 100000 | 3.7e-05 USD | 1 GB Second |
| 2026-10-03 | Functions Flex Consumption on-demand executions | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Functions' and productName eq 'Flex Consumption' and skuName eq 'On Demand' and meterName eq 'On Demand Total Executions'` | Flex Consumption / On Demand / On Demand Total Executions | from 0 | 0 USD | 10 |
| 2026-10-03 | Functions Flex Consumption on-demand executions | `armRegionName eq 'southeastasia' and priceType eq 'Consumption' and serviceName eq 'Functions' and productName eq 'Flex Consumption' and skuName eq 'On Demand' and meterName eq 'On Demand Total Executions'` | Flex Consumption / On Demand / On Demand Total Executions | from 25000 | 4e-06 USD | 10 |

Notes on reading the table:
- The free first slices visible in the API match the documented grants below: Flex execution time is free for the first 100,000 GB-seconds and executions free for the first 25,000 units of 10 (= 250,000); Log Analytics ingestion is free for the first 5 GB.
- Service Bus Standard operations: the API shows the tier boundaries (0, 13, 100, 2500 in millions). Whether the first 13 M operations are "included in the base unit" is what the tier boundary suggests, but this page does not rely on it.
- Not looked up (not found in what was queried, so no number is given): Application Insights is billed as Log Analytics ingestion when workspace-based; the price of a log-search **alert rule** and of action-group emails. The Azure Monitor pricing page fetched on 2026-10-03 did not display them, so the alert cost is **unknown**.
- Functions Flex "Always Ready" meters exist in the API but are not used: the app does not configure always-ready instances.

## Free grants (quoted from official sources)

| Service | Grant | Source |
|---|---|---|
| Azure SQL Database free offer | "For each database, you get 100,000 vCore seconds, 32 GB of data, and 32 GB of backup storage free per month for the lifetime of your subscription. Each Azure subscription allows you to create up to 10 General Purpose databases." With auto-pause behaviour, "each database can be auto-paused until the beginning of the next calendar month"; limits then: max 4 vCores, 32 GB. | <https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql> |
| Functions Flex Consumption (on-demand) | "250,000 executions and 100,000 GB-s of resource consumption per month per subscription" (pay-as-you-go subscriptions only) | <https://azure.microsoft.com/en-us/pricing/details/functions/> |
| Log Analytics (Analytics Logs) | "The first 5 GB/month per billing account in this tier are free" (Analytics Logs only). Whether it applies to workspace-based Application Insights is **not stated** on that page's visible text; the API's own tier boundary (free to 5 GB) is consistent with it. | <https://azure.microsoft.com/en-us/pricing/details/monitor/> |
| App Service F1 | The Retail Prices API lists F1 at 0 USD per hour (table above). The F1 usage quotas are **not quoted here** (not fetched). | table above |

**Discrepancy to resolve at deploy time:** the Bicep reference for `useFreeLimit` says "Allowed on one database in a subscription" (<https://learn.microsoft.com/en-us/azure/templates/microsoft.sql/2025-01-01/servers/databases>), while the free-offer page says up to 10 databases per subscription. PlantOps uses one database, so it is unaffected either way.

## Assumptions

- 730 hours per month (the multiplier for hourly meters).
- Tiny demo traffic: a handful of users, timers that run a few times a day. No operation counts, request rates, log volumes or database seconds were measured, so **no variable usage is priced below**.
- One region: southeastasia. USD list prices, no discounts, no tax, no reservations.
- One Service Bus Standard namespace, one App Service plan, one Flex Consumption function app, one SQL database on the free offer with auto-pause at the limit (so SQL cannot bill).

## List-price floor per web-plan option

"Floor" = the fixed monthly charges that exist whether or not anyone uses the system. It is **list price x the 730 h assumption**, not an estimate of the real bill.

| Component | Basis | B1 option | F1 option |
|---|---|---|---|
| App Service plan | B1: 0.018 USD/h x 730 h; F1: 0 USD/h | 13.14 USD | 0.00 USD |
| Service Bus Standard base unit | 10 USD/month (the monthly meter; the hourly meter 0.013441 x 730 = 9.81 USD is shown for comparison only) | 10.00 USD | 10.00 USD |
| Azure SQL (free offer, AutoPause) | Free grant; nothing billed while the grant lasts, pauses when exhausted | 0.00 USD | 0.00 USD |
| Functions Flex Consumption | Free grant covers the first 250,000 executions and 100,000 GB-s; no fixed fee, no always-ready instances | 0.00 USD at or below the grant | 0.00 USD at or below the grant |
| Key Vault Standard, Storage, Log Analytics, alert rules | No fixed monthly fee found in the queried meters. Usage-based, so not priced here (see formulas below) | not priced | not priced |
| **Fixed floor** | | **23.14 USD / month** | **10.00 USD / month** |

Usage formulas for the variable meters (unit price x your own measured quantity, quantity is *not* known yet):
- Storage data: 0.02 USD x GB stored per month (first tier). Example, for illustration only: 1 GB = 0.02 USD.
- Storage operations: 0.05 USD per 10,000 writes, 0.004 USD per 10,000 reads or other operations.
- Key Vault operations: 0.03 USD per 10,000 operations.
- Log Analytics: 0 USD for the first 5 GB/month, then 2.99 USD per GB. The workspace has a 1 GB/day ingestion cap in `deploy/azure/modules/monitoring.bicep`, so the cap bounds ingestion to at most 30 GB in a 30-day month: a worst-case ceiling of (30 - 5) x 2.99 = 74.75 USD **if the cap were hit every day** (arithmetic on the list price, not a forecast).
- Azure SQL beyond the free grant: not billed with `AutoPause`. If the behaviour were changed to bill the overage, 0.620892 USD per vCore-hour.

F1 caveat (a behavioural cost, not a price): no Always On, so the in-process outbox dispatcher sleeps when the app idles; the timers in the function app still run.

## Actual spend: not measured

Nothing has been deployed, so there is no actual spend. Deploy, let it run for 30 days, then read **Cost Management + Billing > Cost analysis** (filter by the resource group) and replace the floor above with the measured figure. Until then every number on this page is list price x a stated assumption.
