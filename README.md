# StockSharp Connectors

This repository contains all the open connectors for StockSharp (S#).

They are open for modification and for adding new ones — feel free to change
the existing connectors or contribute your own.

## Documentation

See the [StockSharp documentation](https://doc.stocksharp.com/en/topics/api/connectors/creating_own_connector.html)
on how to create your own connector.

## HTTP rate limits

REST clients share server-requested pauses between connector instances in the same process.
HTTP 429 and 418 responses honor the full `Retry-After` value, including HTTP dates;
missing or invalid values use a thirty-second pause. HTTP 5xx responses carrying
`Retry-After` also pause requests to that server. Requests already in flight can complete.
Waiting is cancellable. The pause handler does not replay requests; each client retains
its existing retry policy. Processes sharing a public IP still need a shared request
budget outside this library.

## Live tests

Live tests run manually through the **Live tests** GitHub Actions workflow or locally with
`STOCKSHARP_LIVE_TESTS=1` and `dotnet test Tests/Tests.csproj --configuration Release --filter "TestCategory=Integration" --blame-hang-timeout 3m -- MSTest.Parallelize.Workers=1`.

Additional access is explicit:

| Variable | Purpose |
| --- | --- |
| `STOCKSHARP_LIVE_REGIONAL_TESTS=1` | Enables Binance live checks, Tokocrypto WebSockets, Bybit REST checks, Binance history lookup across futures markets and COIN-M order book metadata, Bybit history lookup, and CoW Protocol checks. Set it on a runner permitted to access those venue APIs. |
| `COINMARKETCAP_TOKEN` | Uses a CoinMarketCap API key. The workflow reads it from the repository secret with the same name. |
| `COINMARKETCAP_KEYLESS_TESTS=1` | Enables CoinMarketCap checks without a key, subject to its anonymous quota. |
| `STOCKSHARP_LIVE_MOEX_DIVIDENDS_TESTS=1` | Enables the separate MOEX dividend-history check on an ISS service providing dividend data. Ordinary ISS security lookup and candle checks do not require it. |
| `STOCKSHARP_LIVE_BYDFI_TESTS=1` | Enables BYDFi futures checks when the published API is accessible. The documented futures routes currently return HTTP 404. |
| `STOCKSHARP_LIVE_GMTRADE_TESTS=1` | Enables GMTrade checks when the runner has access to keeper REST and WebSocket API quota. Anonymous requests currently return HTTP 429. |
| `STOCKSHARP_LIVE_REUTERS_RSS_TESTS=1` | Enables the Thomson Reuters feed when the runner has programmatic access to it. Other RSS feeds remain in the default live run. |

The workflow exposes `include_regional_tests`, `include_keyless_cmc_tests`,
`include_moex_dividends_tests`, `include_bydfi_tests`, `include_gmtrade_tests`, and
`include_reuters_rss_tests`, all disabled by default. Checks that lack the required access
report **Skipped** with the variable to set. The regional and CoinMarketCap options do not
enable MOEX dividends.
Public Binance and Bybit archives and Bybit WebSocket checks remain in the default live run.
Once a check is enabled, HTTP errors, missing data and parsing failures fail the test.

## Support

Questions and support: [chat](https://stocksharp.com/en/chat/).
