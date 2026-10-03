#!/usr/bin/env bash
# Prints a markdown table of Azure list prices for the PlantOps SKUs, straight from the public Azure Retail Prices API
# (read-only, no login): https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices
# Usage: deploy/azure/cost/prices.sh [region]     (default southeastasia)
# Needs: bash, curl, python3 (python3 only parses the API's JSON). It estimates nothing: it only reports what the API returns.
set -euo pipefail

REGION="${1:-southeastasia}"
API="https://prices.azure.com/api/retail/prices"
DATE="$(date -u +%Y-%m-%d)"

# row <label> <odata filter without the region/priceType part> [per-second]
row() {
  local label="$1" filter="$2" persecond="${3:-}"
  local full="armRegionName eq '${REGION}' and priceType eq 'Consumption' and ${filter}"
  python3 - "$API" "$label" "$full" "$DATE" "$persecond" <<'PY'
import json, sys, urllib.parse, urllib.request

api, label, flt, date, persecond = sys.argv[1:6]
url = api + "?" + urllib.parse.urlencode({"$filter": flt})
items = []
while url:
    with urllib.request.urlopen(url, timeout=60) as resp:
        body = json.load(resp)
    items += body.get("Items", [])
    url = body.get("NextPageLink")

esc = lambda s: s.replace("|", "\\|")
if not items:
    print(f"| {date} | {esc(label)} | `{esc(flt)}` | NOT FOUND in the API | - | - | - |")
    sys.exit(0)

seen = set()
for i in sorted(items, key=lambda x: (x["meterName"], x["unitOfMeasure"], x["tierMinimumUnits"], x["retailPrice"])):
    key = (i["meterName"], i["unitOfMeasure"], i["tierMinimumUnits"], i["retailPrice"], i["skuName"])
    if key in seen:
        continue
    seen.add(key)
    meter = f'{i["productName"]} / {i["skuName"]} / {i["meterName"]}'
    price = f'{i["retailPrice"]:g} {i["currencyCode"]}'
    unit = i["unitOfMeasure"]
    if persecond and unit.strip().lower() == "1 hour":
        price += f' (= {i["retailPrice"] / 3600:.6g} per vCore-second)'
    print(f'| {date} | {esc(label)} | `{esc(flt)}` | {esc(meter)} | from {i["tierMinimumUnits"]:g} | {price} | {esc(unit)} |')
PY
}

echo "Region: ${REGION}. Currency: USD. Prices are list (retail) prices, not what a particular agreement pays."
echo
echo "| Date | Item | API filter (plus region and priceType=Consumption) | Meter (product / sku / meter) | Tier starts at | Unit price | Unit |"
echo "|---|---|---|---|---|---|---|"

row "App Service Linux B1" \
    "serviceName eq 'Azure App Service' and productName eq 'Azure App Service Basic Plan - Linux' and skuName eq 'B1'"
row "App Service Linux F1" \
    "serviceName eq 'Azure App Service' and productName eq 'Azure App Service Free Plan - Linux' and skuName eq 'F1'"
row "Service Bus Standard base unit" \
    "serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Base Unit'"
row "Service Bus Standard operations" \
    "serviceName eq 'Service Bus' and skuName eq 'Standard' and meterName eq 'Standard Messaging Operations'"
row "Key Vault Standard operations" \
    "serviceName eq 'Key Vault' and productName eq 'Key Vault' and skuName eq 'Standard' and meterName eq 'Operations'"
row "Storage (StorageV2, Hot LRS) data stored" \
    "serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Data Stored'"
row "Storage (StorageV2, Hot LRS) write operations" \
    "serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot LRS Write Operations'"
row "Storage (StorageV2, Hot LRS) read operations" \
    "serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'Hot Read Operations'"
row "Storage (StorageV2, Hot LRS) other operations" \
    "serviceName eq 'Storage' and productName eq 'General Block Blob v2' and skuName eq 'Hot LRS' and meterName eq 'All Other Operations'"
row "Log Analytics ingestion (Analytics Logs)" \
    "serviceName eq 'Log Analytics' and skuName eq 'Analytics Logs' and meterName eq 'Analytics Logs Data Ingestion'"
row "Azure SQL serverless General Purpose Gen5 vCore (beyond the free grant)" \
    "serviceName eq 'SQL Database' and productName eq 'SQL Database General Purpose - Serverless - Compute Gen5' and meterName eq 'vCore'" per-second
row "Functions Flex Consumption on-demand execution time" \
    "serviceName eq 'Functions' and productName eq 'Flex Consumption' and skuName eq 'On Demand' and meterName eq 'On Demand Execution Time'"
row "Functions Flex Consumption on-demand executions" \
    "serviceName eq 'Functions' and productName eq 'Flex Consumption' and skuName eq 'On Demand' and meterName eq 'On Demand Total Executions'"
