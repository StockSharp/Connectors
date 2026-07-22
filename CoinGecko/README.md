# StockSharp CoinGecko Connector

The connector integrates StockSharp with the current CoinGecko REST API and
the official CoinGecko WebSocket beta. It publishes aggregated CoinGecko coins
and GeckoTerminal on-chain pools as separate instruments, so provider-wide
reference prices are never mixed with pool-level swaps or OHLCV.

## Access and configuration

Create a CoinGecko API key and select the matching `Tier`:

- `Demo` uses `https://api.coingecko.com/api/v3` and the
  `x-cg-demo-api-key` header;
- `Pro` uses `https://pro-api.coingecko.com/api/v3` and the
  `x-cg-pro-api-key` header.

The adapter always sends keys in headers. `QuoteCurrency` is any ID returned by
the supported-currencies endpoint and defaults to `usd`. `RequestInterval`
provides client-side pacing; its two-second default is conservative for the
Demo plan and can be reduced to match a paid plan's limit.

The official WebSocket is currently beta and requires a Pro key on Analyst
plan or above. It connects to `wss://stream.coingecko.com/v1`, authenticates by
header, restores active subscriptions after reconnect, and shares identical
upstream streams between StockSharp subscribers. Disable `IsStreamingEnabled`
for REST-only use. A Demo or insufficiently entitled key remains fully usable
for its REST features but cannot create a live subscription.

## Instruments and market data

`COINGECKO` securities represent aggregated coins. Their native identity keeps
the unique CoinGecko coin ID and selected quote currency, avoiding collisions
between duplicate symbols. They support:

- complete active-coin discovery from `/coins/list`;
- current price, 24-hour high, low, volume, and percentage change;
- live Level 1 through the `CGSimplePrice` WebSocket channel;
- hourly and daily historical OHLC range requests on eligible Pro plans,
  including locally aggregated 2h, 4h, 8h, 12h, and 4d candles;
- recent Demo OHLC at the provider's native automatic granularities: 30m for
  one day, 4h for 3–30 days, and 4d for longer windows.

`CGONCHAIN` securities represent a specific GeckoTerminal liquidity pool while
pricing its base token in USD. Search requires a token name, symbol, contract,
or pool address; `OnchainNetwork` can restrict results. The native identity
keeps network, pool address, base-token address, symbol, DEX, and pool name.
They support:

- current base-token USD price, 24-hour USD volume, and percentage change;
- recent pool swaps from REST and live swaps through `OnchainTrade`;
- historical pool OHLCV from 1 minute through 4 days on Demo, plus 1s, 15s,
  and 30s resolutions on Pro, with exact aggregation where CoinGecko exposes a
  smaller source interval;
- live base-token USD price through `OnchainSimpleTokenPrice`;
- live 1s, 1m, 5m, 15m, 1h, 2h, 4h, 8h, 12h, and 1d OHLCV through
  `OnchainOHLCV`.

The REST 15s and 30s resolutions and the locally aggregated 30m and 4d pool
intervals are historical only because the WebSocket does not expose them.

On-chain REST trade history is limited by CoinGecko to the latest 300 swaps in
the past 24 hours. Pool OHLCV pages contain at most 1,000 rows; the adapter
walks backward by `before_timestamp`, deduplicates boundaries, and applies the
configured `HistoryLimit`. Coin range calls are split into the documented
31-day hourly or 180-day daily windows.

## Operational boundaries

CoinGecko is a data aggregator, not an execution venue. The adapter therefore
does not expose portfolios, orders, withdrawals, or synthetic transaction
support. Aggregated coin prices are reference data rather than executable
quotes. On-chain pool prices, swaps, and candle volumes are USD-denominated;
pool candle volume is the API's USD volume, not base-token quantity.

WebSocket access, historical depth, endpoint availability, cache frequency,
credits, and rate limits depend on the account plan. The WebSocket beta is
outside CoinGecko's API SLA and charges credits per response. The adapter
surfaces entitlement failures and does not silently replace a requested live
stream with polling.

All API times are normalized to UTC `DateTime` values.

When displaying CoinGecko or GeckoTerminal data, follow the provider's
attribution requirements.

## Official documentation

- [CoinGecko API documentation](https://docs.coingecko.com/)
- [API key setup](https://docs.coingecko.com/docs/setting-up-your-api-key)
- [Pro API authentication](https://docs.coingecko.com/reference/authentication)
- [API endpoint overview](https://docs.coingecko.com/reference/endpoint-overview)
- [Coins list](https://docs.coingecko.com/reference/coins-list)
- [Coins market data](https://docs.coingecko.com/reference/coins-markets)
- [Coin OHLC](https://docs.coingecko.com/reference/coins-id-ohlc)
- [Coin OHLC range](https://docs.coingecko.com/reference/coins-id-ohlc-range)
- [On-chain pool search](https://docs.coingecko.com/reference/search-pools)
- [Pool trades](https://docs.coingecko.com/reference/pool-trades-contract-address)
- [Pool OHLCV](https://docs.coingecko.com/reference/pool-ohlcv-contract-address)
- [WebSocket overview](https://docs.coingecko.com/websocket/index)
- [CGSimplePrice channel](https://docs.coingecko.com/websocket/cgsimpleprice)
- [OnchainSimpleTokenPrice channel](https://docs.coingecko.com/websocket/onchainsimpletokenprice)
- [OnchainTrade channel](https://docs.coingecko.com/websocket/onchaintrade)
- [OnchainOHLCV channel](https://docs.coingecko.com/websocket/onchainohlcv)
- [Official attribution guide](https://brand.coingecko.com/resources/attribution-guide)
