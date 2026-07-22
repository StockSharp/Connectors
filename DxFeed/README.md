# StockSharp dxFeed connector

The connector provides direct access to dxFeed market data through the official
dxLink WebSocket protocol. It does not load the Graal-based native dxFeed SDK and
does not start an external process.

## Supported data

- real-time Level 1 from `Quote`, `Trade`, `TradeETH`, `Summary`, and `Profile`;
- option analytics from `Greeks`, `TheoPrice`, and `Underlying` events;
- tick-by-tick trades and history from `TimeAndSale`;
- historical and streaming time-frame candles;
- full depth snapshots through the dedicated dxLink `DOM` service;
- indexed order events exposed as StockSharp order-log messages for every selected
  dxFeed order source;
- exact-symbol security lookup enriched by `Profile` data;
- automatic dxLink authorization, keepalive, reconnect, channel reopening, and
  subscription restoration.

The connector requests the `FULL` wire format so field names remain explicit and rejects a server configuration that switches the feed to the compact array format.

## Connection

The default address is the public demo endpoint:

```text
wss://demo.dxfeed.com/dxlink-ws
```

For production or trial access, replace `Address` with the endpoint supplied by
dxFeed and set `Token`. A token can be left empty only when the selected endpoint
allows anonymous access. Available venues, event types, history depth, and
real-time versus delayed delivery are determined by the account's subscriptions
and exchange entitlements.

`MarketDepthSources` is a comma-separated list of dxFeed order sources used for
DOM channels. Its default is `NTV`, matching the official dxLink DOM example.
Set the source or sources provided for the required venue. `MarketDepthLevels`
controls the requested snapshot depth, and `AggregationPeriod` controls optional
server-side conflation.

## Limitations

- dxFeed is a market-data service; this connector intentionally exposes no order,
  portfolio, or trading operations.
- The dxLink feed protocol does not provide a complete instrument-catalog search.
  Security lookup therefore accepts explicit symbols only. Use the instrument
  profile/reference-data delivery included in the commercial agreement when a
  complete catalog is required.
- DOM availability depends on both the symbol and the selected order source.
- Candle and `TimeAndSale` history use dxLink time-series snapshots. The provider
  may finish a snapshot with `SNAPSHOT_SNIP` when an entitlement or server limit
  truncates the result.
- Candle periods exposed by the adapter are whole-second time frames. Other
  dxFeed candle types (volume, price, tick, option-expiration, formula, and custom
  attributes) are not represented by StockSharp's time-frame candle subscription.

## Official documentation

- [dxLink overview](https://kb.dxfeed.com/en/market-data-api/dxlink.html)
- [dxLink WebSocket protocol and debug console](https://kb.dxfeed.com/en/market-data-api/data-access-solutions/websocket.html)
- [Market Data API](https://kb.dxfeed.com/en/market-data-api.html)
- [Market event model](https://kb.dxfeed.com/en/data-model/market-events.html)
- [Candle request format](https://kb.dxfeed.com/en/data-services/aggregated-services/how-to-request-candles.html)
- [Indexed-event snapshot flags](https://docs.dxfeed.com/dxfeed/api/com/dxfeed/event/IndexedEvent.html)
- [dxFeed press kit and official logo assets](https://dxfeed.com/press-kit/)
