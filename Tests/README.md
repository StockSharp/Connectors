# Connector tests

Each connector has one fixture. A fixture can contain both offline regression tests and
live checks. Live methods carry the `Integration` category and read their switches
through `BaseTestClass.Env(...)`.

Run the offline suite with `--filter "TestCategory!=Integration"`. To run anonymous
live checks after building Release:

```sh
STOCKSHARP_LIVE_TESTS=1 dotnet test Tests/Tests.csproj --no-build --configuration Release --filter "TestCategory=Integration" --logger "trx;LogFileName=live-tests.trx" --results-directory TestResults --blame-hang-timeout 3m -- MSTest.Parallelize.Workers=1
```

Without `STOCKSHARP_LIVE_TESTS=1`, live methods are skipped before connecting. This
does not disable offline methods in the same fixture. Tests do not change process
environment variables.

`LiveMarketDataTestBase` checks securities, Level 1, order books, trades and candles
where the adapter and the anonymous feed support them. Fixtures supply a public
instrument, a supported candle interval and any required lookup criteria. An empty
response, parsing error or unexpected HTTP failure fails the check.

Reference-only services have explicit lookup or filing tests instead of inheriting
unsupported quote checks. These include Coin Metrics Community, OpenFIGI, GLEIF,
ESMA FIRDS, SEC EDGAR and filings.xbrl.org. SEC EDGAR also checks company facts.
The MOEX contest connector checks its historical order-log archive.

Some public services need additional access:

- `STOCKSHARP_LIVE_REGIONAL_TESTS=1` enables fixtures marked `RegionalLiveTests`.
  This includes endpoints blocked by the runner's region, WAF, DNS or shared-IP
  quota. Newly covered services in this group are Bithumb, BloFin, Bullish,
  Coinhako, NovaDAX, Paradex, Pintu Pro, ProBit, Tapbit and the MOEX FTP archive.
- Existing provider-specific switches for CoinMarketCap, MOEX dividends, BYDFi,
  GMTrade and Reuters RSS remain in force.
  See `LiveTestAccess` and the corresponding fixture for the switch and reason.
- Quidax recent trades require a key. Its securities, quotes, order book and
  candles are checked anonymously.
- PancakeSwap and QuickSwap checks use public RPC for securities and quote probes.
  Swaps and candles need a configured subgraph and are outside these anonymous
  fixtures.
- The current Uniswap adapter requires a Trading API key for quotes and a Graph
  key for swaps and candles. Its anonymous test checks token reference data through
  the connector's RPC client; it does not claim to test anonymous adapter quotes.

An API key available through free registration still counts as authenticated access.
Account feeds, licensed datasets, local terminals and closed venues are not added
to the anonymous suite.
