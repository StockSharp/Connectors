# StockSharp Pyth Pro Connector

The connector integrates StockSharp with the current Pyth Pro symbology,
history, latest-price, and redundant WebSocket APIs. It is a read-only market
data connector and does not submit orders or publish prices on-chain.

## Access and endpoints

Set `Token` to a Pyth Pro API key. It is sent only as an
`Authorization: Bearer` header and is never placed in a URL. The production
defaults are:

- `https://pyth.dourolabs.app/v1/` for symbology and OHLC history;
- `https://pyth-lazer.dourolabs.app/v1/` for latest prices;
- `wss://pyth-lazer-{0,1,2}.dourolabs.app/v1/stream` for live prices.

Pyth requires consumers to connect to all three WebSocket routers so a
deployment or endpoint outage does not interrupt delivery. The connector keeps
all three connections active, resubscribes after reconnects, and deduplicates
identical feed updates by feed timestamp and values. A live subscription is
terminated only when all three routers exhaust their reconnection attempts.

At connection time the adapter loads `/symbols`. `IsEntitledOnly` defaults to true and requests only feeds available to the configured token. `IsIncludeInactive` can additionally expose inactive and coming-soon metadata; stable feeds are the default. The feed ID is preserved as the native StockSharp security identity.

## Market data

Security lookup covers crypto, equities, FX, commodities, metals, futures,
indices, rates, and NAV feeds. Pyth funding-rate feeds are intentionally omitted
because StockSharp Level 1 has no exact funding-rate field. Futures expiration
and the decimal price step derived from the Pyth exponent are retained.

Level 1 subscriptions receive an authenticated REST snapshot followed by live
WebSocket updates. Price, best bid, best ask, market session, and the per-feed
`feedUpdateTimestamp` map directly to StockSharp fields. The per-feed timestamp
is used as server time so a carried-forward price is not presented as freshly
generated. Experimental Pyth best bid and ask values are passed through only
when present and must form a valid non-crossed spread. Confidence, publisher
count, EMA values, and signed blockchain payloads are not relabeled as
unrelated StockSharp fields.

Historical Level 1 requests use exact one-minute aggregate closes. Historical
candles use the TradingView-compatible history API at 1, 2, 5, 15, and 30
minutes; 1, 2, 4, 6, and 12 hours; and 1 day or 1 week. Responses are validated
as aligned arrays and split into bounded requests. The current unfinished
bar is not emitted as a finished candle. `HistoryLimit`, `HistoryLookback`, and
`MaximumBarsPerRequest` bound downloads.

`Channel` is a preference. Each feed advertises `min_channel`; when the chosen
channel is too fast for a feed, the connector automatically selects that
feed's slower minimum channel. The supported production values are real-time,
50 ms, 200 ms, and 1000 ms.

Response and WebSocket message sizes are bounded, WebSocket UTF-8 is strict, timestamps become UTC `DateTime`, and API credentials are redacted from errors.

## Official documentation

- [Pyth Pro overview](https://docs.pyth.network/price-feeds/pro)
- [Symbology and reference data](https://docs.pyth.network/price-feeds/pro/symbology-reference)
- [History API](https://docs.pyth.network/price-feeds/pro/api/history)
- [REST API](https://docs.pyth.network/price-feeds/pro/api/rest)
- [WebSocket API](https://docs.pyth.network/price-feeds/pro/api/websocket)
- [Subscribe to prices](https://docs.pyth.network/price-feeds/pro/subscribe-to-prices)
- [Payload reference](https://docs.pyth.network/price-feeds/pro/payload-reference)
- [Pyth examples](https://github.com/pyth-network/pyth-examples/tree/main/lazer)
