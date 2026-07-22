# StockSharp FXCM connector

The connector integrates StockSharp with FXCM's published [REST and Socket.IO API](https://fxcm-api.readthedocs.io/en/latest/). REST is used for snapshots, historical candles and trading commands. The persistent Socket.IO session supplies native quote and trading-table updates and also provides the session identifier required for REST authorization.

## Supported functionality

- Live and demo environments.
- Persistent access-token authentication.
- Instrument lookup with FXCM offer identifiers, instrument types, precision and pip size.
- Native real-time bid/ask, session high and session low updates.
- Historical midpoint candles for all periods documented by FXCM, from one minute through one month.
- Account balance, equity, usable margin and P/L.
- Open-position snapshots and native position/account table updates.
- Market trades and entry orders, including market-range and range-entry slippage.
- Stop-loss, take-profit and trailing-step parameters, either as prices or pip distances.
- Working-order replacement and cancellation.
- Full close of an open FXCM trade when its trade id is passed to order cancellation.
- Current orders, closed-position history and native order/open-position/closed-position table updates.

## Configuration

1. Create a persistent access token in Trading Station Web as described in the FXCM API documentation.
2. Set `Token` on `FxcmMessageAdapter`.
3. Keep `IsDemo` enabled for `api-demo.fxcm.com`, or disable it for `api.fxcm.com`.
4. Use the FXCM account id as `PortfolioName` when placing an order.

FXCM REST authorization is session-bound. The connector first establishes the documented Socket.IO connection, then sends `Bearer {socket_id}{access_token}` on REST requests. If the stream reconnects, the connector creates a new REST session and restores active quote and trading-table subscriptions.

## Market-data details

FXCM's public stream publishes one best bid and ask plus session high and low. It does not publish exchange trades or an order-book ladder, so the connector deliberately does not advertise tick trades or market depth and does not fabricate either data type.

The candles endpoint returns separate bid and ask OHLC values. The connector exposes their midpoint and uses FXCM's tick quantity as candle volume. The documented API does not provide a native candle stream; candle subscriptions therefore return REST history and finish instead of synthesizing live bars from quotes.

## Trading details and limits

- FXCM's `amount`/`amountK` values use the account's FXCM amount convention, commonly thousands of base-currency units. The connector passes StockSharp volume through without silently rescaling it.
- A market order uses `AtMarket`, or `MarketRange` when `Slippage` is supplied.
- A non-market order uses `Entry`, or `RangeEntry` when `Slippage` is supplied.
- `IsInPips` controls whether stop-loss and take-profit values are absolute rates or pip distances.
- Cancelling a working order calls `delete_order`. If the supplied id matches a cached open trade id, cancellation performs a full `close_trade` instead.
- Instrument availability, trading permissions, leverage, rate limits and supported order parameters depend on the FXCM account and jurisdiction.

## Official documentation

- [FXCM REST API and Socket.IO documentation](https://fxcm-api.readthedocs.io/en/latest/)
- [FXCM Socket REST API specification](https://fxcm-api.readthedocs.io/en/latest/_downloads/3274e66603a66e9c35309035e7930902/Socket%20REST%20API%20Specs.pdf)
- [FXCM API trading overview](https://www.fxcm.com/eu/algorithmic-trading/api-trading/)
- [FXCM official GitHub organization](https://github.com/fxcm)

FXCM and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by FXCM.
