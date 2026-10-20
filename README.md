# StockSharp Connectors

This repository contains all the open connectors for StockSharp (S#).

They are open for modification and for adding new ones — feel free to change
the existing connectors or contribute your own.

## Documentation

See the [StockSharp documentation](https://doc.stocksharp.com/en/topics/api/connectors/creating_own_connector.html)
on how to create your own connector.

## Live tests

Live tests run manually through the **Live tests** GitHub Actions workflow or locally with
`STOCKSHARP_LIVE_TESTS=1` and `dotnet test Tests/Tests.csproj --configuration Release --filter "TestCategory=Integration" --blame-hang-timeout 3m -- MSTest.Parallelize.Workers=1`.

Additional access is explicit:

| Variable | Purpose |
| --- | --- |
| `STOCKSHARP_LIVE_REGIONAL_TESTS=1` | Enables Binance live checks, Tokocrypto WebSockets, Bybit REST checks, Binance history lookup across futures markets and COIN-M order book metadata, and Bybit history lookup. Set it on a runner permitted to access those venue APIs. |
| `COINMARKETCAP_TOKEN` | Uses a CoinMarketCap API key. The workflow reads it from the repository secret with the same name. |
| `COINMARKETCAP_KEYLESS_TESTS=1` | Enables CoinMarketCap checks without a key, subject to its anonymous quota. |

The workflow exposes `include_regional_tests` and `include_keyless_cmc_tests`, both disabled by
default. Checks that lack the required access report **Skipped** with the variable to set.
Public Binance and Bybit archives and Bybit WebSocket checks remain in the default live run.
Once a check is enabled, HTTP errors, missing data and parsing failures fail the test.

## Support

Questions and support: [chat](https://stocksharp.com/en/chat/).
