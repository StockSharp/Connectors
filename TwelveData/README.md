# Twelve Data connector for StockSharp

This connector integrates StockSharp with Twelve Data's official REST API and real-time WebSocket. It provides market data for stocks, ETFs, forex, cryptocurrencies, and commodities. Twelve Data is a data provider rather than a broker in this API, so the connector does not expose portfolios or order routing.

## Supported functionality

- API-key authentication through the documented REST `Authorization: apikey ...` header and WebSocket `apikey` query parameter.
- Stock, ETF, forex-pair, cryptocurrency, and commodity reference lists plus cross-market symbol search.
- Native identifiers that preserve the market family, exact symbol, exchange, and MIC required to disambiguate listings.
- Current quote snapshots with last price, session OHLC, previous close, daily volume, percent change, and 52-week high and low.
- Genuine WebSocket Level1 updates with price, daily cumulative volume, and bid/ask when those fields are available for the instrument.
- Extended WebSocket subscription objects with `exchange` and `mic_code`, avoiding an ambiguous symbol-only subscription when lookup supplied venue identifiers.
- Native 1, 5, 15, 30, and 45-minute candles; 1, 2, 4, and 8-hour candles; and daily, weekly, and monthly candles.
- Historical pagination at the documented 5,000-value REST limit, duplicate-boundary removal, exchange-time-zone conversion for end-of-day bars, transient retry handling, bounded WebSocket frames, reconnect, heartbeat, and automatic subscription restoration.

## Configuration

- `Token` is the API key from the Twelve Data dashboard.
- `Address` defaults to the official `https://api.twelvedata.com/` REST base address.
- `WebSocketAddress` defaults to `wss://ws.twelvedata.com/v1/quotes/price`.
- `Country` defaults to `United States` and narrows unfiltered stock and ETF reference downloads.
- `StockExchange` and `StockMic` are optional stock/ETF lookup filters. Leave both empty to retain all venues in the selected country.
- `CryptoExchange` optionally narrows cryptocurrency lookup and disambiguates a venue-specific stream.
- `Adjustment` selects all, split-only, dividend-only, or no historical adjustment. The default is split adjustment.
- `IsPrePost` requests eligible US extended-hours data from REST endpoints. WebSocket extended-hours availability follows the account and instrument coverage.

Use securities returned by lookup whenever possible. The StockSharp `Native` identifier retains the exact provider identity while `SecurityCode` remains the familiar ticker or pair.

## Data semantics and limitations

Twelve Data access is plan-, exchange-, and instrument-dependent. The free Basic plan has small REST and trial WebSocket credit limits; a valid API key does not imply access to all markets or symbols. Entitlement and rate-limit errors are returned to StockSharp rather than treated as empty data.

A Level1 subscription first requests one REST quote snapshot and then opens the genuine WebSocket stream unless it is history-only or its requested count is already satisfied. Twelve Data does not expose historical Level1 event sequences, so subscriptions with `From` or `To` are rejected. The WebSocket `day_volume` field is cumulative session volume and is mapped to StockSharp `Volume`, never to a fabricated last-trade size.

The price stream can include best bid and ask for some asset classes, but it does not provide a multi-level order book. The connector therefore advertises Level1 and not market depth. It also does not advertise ticks: a price update is not universally documented as an executed trade and daily cumulative volume cannot be converted into reliable trade size.

The connector uses the extended WebSocket subscription form when venue identity is known. Some manually entered symbol-only securities remain inherently ambiguous; obtaining them through security lookup is the reliable way to select a listing.

Twelve Data returns intraday timestamps in UTC when requested, while daily, weekly, and monthly timestamps remain in the exchange time zone. The connector converts both forms to UTC and computes end-of-day close boundaries in local exchange time so daylight-saving transitions are preserved.

## Official documentation

- [Twelve Data API overview](https://twelvedata.com/docs/introduction/overview)
- [Twelve Data API documentation](https://twelvedata.com/docs)
- [WebSocket documentation](https://twelvedata.com/docs#websocket)
- [WebSocket streaming guide](https://support.twelvedata.com/en/articles/5620516-how-to-stream-the-data)
- [WebSocket limits and payload scope](https://support.twelvedata.com/en/articles/5194610-websocket-faq)
- [Plans and API/streaming credits](https://twelvedata.com/pricing)
- [Official Twelve Data Python client](https://github.com/twelvedata/twelvedata-python)
- [StockSharp Twelve Data connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/twelvedata.html)

Data provided by Twelve Data. Twelve Data and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Twelve Data.
