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

AlorHistory and MOEX ISS check hourly candles over the preceding two weeks so
weekends and exchange holidays do not leave the requested history empty. Gemini
looks up BTCUSD, the same instrument used by its subscriptions, because loading the
entire catalog requires a separate details request for each pair.

Reference-only services have explicit lookup or filing tests instead of inheriting
unsupported quote checks. These include Coin Metrics Community, OpenFIGI, GLEIF,
ESMA FIRDS, SEC EDGAR and filings.xbrl.org. SEC EDGAR also checks company facts.
The MOEX contest connector checks its historical order-log archive.

Some public services need additional access:

- `STOCKSHARP_LIVE_REGIONAL_TESTS=1` enables fixtures marked `RegionalLiveTests`
  and the Smart-Lab RSS case.
  This includes endpoints blocked by the runner's region, WAF, DNS or shared-IP
  quota. Newly covered services in this group are Bithumb, BloFin, Bullish,
  Coinhako, NovaDAX, Paradex, Pintu Pro, ProBit, Tapbit and the MOEX FTP archive.
  It also enables dxFeed demo depth, trades and candles, which do not reliably
  arrive on hosted runners, ESMA FIRDS, whose public gateway can time out, and
  LFJ swap and candle history, which requires many sequential public RPC reads.
  LFJ securities and quotes remain enabled without this additional switch.
  LATOKEN also requires this switch while its public WebSocket returns HTTP 522
  during connection setup, preventing all five market data checks from starting.
  AltCoinTrader Level 1, depth and trades require it because anonymous snapshot
  requests can return HTTP 429 without Retry-After; its security lookup remains
  enabled. Coincheck requires it while its public API returns HTTP 503 maintenance
  responses and the public stream does not deliver market data.
  Smart-Lab RSS requires it because requests from hosted runners can time out
  before a response arrives. MOEX and MarketWatch RSS run without this switch.
  Cetus ticks require it while the public Sui checkpoint stream can be reset by
  the HTTP/2 server. Cetus securities and quote checks remain enabled by default.
  All five Injective market data checks require it while the mainnet Indexer
  returns HTTP 503 `no healthy upstream`, preventing connection setup.
- `STOCKSHARP_LIVE_BTCTURK_HISTORY_TESTS=1` enables BtcTurk candles. Its published
  graph API currently returns a null status and empty bars even for documented
  requests. The remaining BtcTurk market checks run without this switch. GitHub
  exposes the switch as `include_btcturk_history_tests`, disabled by default.
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
