# Finnhub connector for StockSharp

This connector integrates StockSharp with Finnhub's official REST API v1 and real-time WebSocket. It covers exchange-listed securities, forex, crypto, candles, tick history, and financial news. It is a market-data connector: Finnhub does not expose brokerage order routing through this API.

## Supported functionality

- API-token authentication through the documented `X-Finnhub-Token` REST header and WebSocket `token` parameter.
- Stock lookup by Finnhub exchange code, optional MIC, symbol, company name, ISIN, or CUSIP search text.
- Configurable forex and crypto source lookup, preserving Finnhub's unique native symbols such as `OANDA:EUR_USD` and `BINANCE:BTCUSDT`.
- Current US stock quote snapshots with last price, session OHLC, previous close, and percent change.
- Optional separately entitled US stock bid/ask snapshots with price, size, and provider timestamp.
- Genuine WebSocket trades and price updates for supported stocks, forex sources, and crypto exchanges. The same native event can feed StockSharp tick and Level1-last-trade subscriptions without opening duplicate Finnhub subscriptions.
- Premium historical US and international stock ticks, paged at Finnhub's documented 25,000-record limit.
- Native 1, 5, 15, 30, and 60-minute candles plus daily, weekly, and monthly candles for stocks, forex, and crypto where entitled.
- Company news by stock symbol and general, forex, crypto, or merger market-news categories.
- Rate-limit and transient-server retries, UTC timestamp validation, bounded WebSocket frames, reconnect, and automatic subscription restoration.

## Configuration

- `Token` is the API key from the Finnhub dashboard.
- `Address` defaults to the official `https://finnhub.io/api/v1/` REST base address.
- `WebSocketAddress` defaults to the official `wss://ws.finnhub.io/` endpoint.
- `StockExchange` defaults to Finnhub's `US` exchange code. `StockMic` can narrow the returned stock list to one MIC.
- `ForexExchange` and `CryptoExchange` select the source used when StockSharp requests currency or crypto securities. The defaults are `OANDA` and `BINANCE`.
- `NewsCategory` applies only to news subscriptions without a security.
- `IsBidAskEnabled` calls the premium `/stock/bidask` endpoint. Leave it disabled when the account is not entitled to that product.

Use securities returned by lookup whenever possible. Their `Native` identifier retains the exact Finnhub symbol required by REST and WebSocket endpoints even when `SecurityCode` contains the more readable display symbol.

## Data semantics and limitations

Finnhub product access is plan- and exchange-dependent. A valid token does not imply access to candles, tick history, international real-time data, last bid/ask, or every WebSocket source. HTTP entitlement errors are returned to StockSharp instead of being hidden behind empty data.

The WebSocket payload contains trades or price updates: symbol, price, timestamp, volume, and conditions. It does not contain a multi-level order book or a universal NBBO stream. The connector therefore advertises ticks and Level1 last-trade updates, not market depth. For forex or crypto sources that publish price-only updates, Finnhub documents volume as zero; the connector leaves trade volume unset rather than inventing it.

A stock Level1 subscription begins with the REST quote snapshot and then uses the genuine WebSocket unless it is history-only or its requested count has already been satisfied. Forex and crypto have no equivalent universal snapshot endpoint, so their Level1 values begin with the first WebSocket update. Historical Level1 event sequences are not available and are rejected.

Stock tick history is a premium date-based REST product. An explicit `From` value is downloaded forward by UTC date. A history-only request without `From` returns the latest requested records found within the preceding 31 calendar days. Forex and crypto historical trades are not exposed by this endpoint and are not synthesized from candles.

Finnhub limits an intraday candle request to one month. The connector splits longer requests into documented-size windows, removes inclusive-boundary duplicates, and preserves the provider's native OHLCV values. Candles and news are finite REST subscriptions; they are never presented as streaming data.

Finnhub permits one WebSocket connection per API key. Run one connected adapter per token and fan out its StockSharp subscriptions rather than opening parallel Finnhub sessions. Finnhub also documents that FXCM, Forex.com, and FHFX do not stream; select another entitled source or use their candle endpoints.

## Official documentation

- [Finnhub API documentation](https://finnhub.io/docs/api)
- [Authentication and rate limits](https://finnhub.io/docs/api/introduction)
- [WebSocket trades and price updates](https://finnhub.io/docs/api/websocket-trades)
- [Stock symbol reference](https://finnhub.io/docs/api/stock-symbols)
- [Current stock quote](https://finnhub.io/docs/api/quote)
- [Last bid and ask](https://finnhub.io/docs/api/stock-bidask)
- [Stock candles](https://finnhub.io/docs/api/stock-candles)
- [Historical ticks](https://finnhub.io/docs/api/stock-tick)
- [Official generated Go SDK](https://github.com/Finnhub-Stock-API/finnhub-go)
- [StockSharp Finnhub connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/finnhub.html)

Finnhub and the Finnhub logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Finnhub.
