# Coins.ph Connector

The connector integrates Coins.ph spot markets through the current REST API,
public WebSocket streams, and the authenticated user-data stream.

Supported features:

- security lookup with trading state, price and quantity steps, and order limits;
- Level1, order-book, public trade, and OHLCV candle snapshots;
- realtime ticker, 200-level depth, trade, and candle subscriptions;
- account balances, active and historical orders, and account trade history;
- market, limit, maker-only, stop-loss, and take-profit orders;
- individual and symbol-wide cancellation plus atomic cancel-replace;
- authenticated balance, order, and fill notifications through a listen key.

Public streams use live `SUBSCRIBE` and `UNSUBSCRIBE` commands and restore all
active subscriptions after reconnect. The connector sends the JSON ping required
by Coins.ph before the five-minute timeout. The user-data listen key is renewed
every 25 minutes.

Set `CoinsPhOrderCondition.QuoteAmount` to submit a quote-currency amount instead
of a base quantity for market orders. Coins.ph requires this field for
market-trigger buy orders. Conditional orders also use `StopPrice` and `Type` to
select stop-loss or take-profit behavior.

API credentials are optional for public market data and required for portfolio
and transaction operations. Signed requests use HMAC-SHA256 over the exact query
string and the `X-COINS-APIKEY` header.

Official documentation:

- [REST API](https://docs.coins.ph/rest-api/)
- [WebSocket streams](https://docs.coins.ph/web-socket-streams/)
- [User-data stream](https://docs.coins.ph/user-data-stream/)
