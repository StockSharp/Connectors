# StockSharp Capital.com connector

The connector integrates StockSharp with the official [Capital.com Public API](https://open-api.capital.com/). It uses the REST trading API and the native Capital.com WebSocket service; streaming is not emulated with REST polling.

## Supported functionality

- Live and demo environments.
- Session authentication with `X-CAP-API-KEY`, `CST` and `X-SECURITY-TOKEN`.
- Optional RSA encryption of the API key custom password.
- Optional selection of a specific financial account.
- Full-market or text-based instrument lookup and detailed market metadata by EPIC.
- Real-time bid/offer prices and quantities through `marketData.subscribe`.
- Historical candles through REST.
- Real-time classic OHLC candles through `OHLCMarketData.subscribe` for 1, 5, 15 and 30 minutes, 1 and 4 hours, 1 day and 1 week.
- Accounts, balances and open CFD positions.
- Market positions and working limit/stop orders.
- Protective stop-loss and take-profit parameters by level, distance or amount, including guaranteed and trailing stops where the account and instrument permit them.
- Order amendments, working-order cancellation and full position close.
- Typed order confirmations and account activity history.

Every REST and WebSocket request and response used by the connector has a dedicated DTO. The implementation does not use `JObject`, `JArray`, `JToken`, `dynamic`, anonymous protocol payloads or protocol dictionaries.

## Configuration

1. Enable two-factor authentication in Capital.com.
2. In **Settings > API integrations**, generate an API key and configure its custom password.
3. Set `ApiKey`, `Login` and `Password` on `CapitalComMessageAdapter`.
4. Keep `IsDemo` enabled for a demo account, or disable it for live trading.
5. Optionally set `AccountId`; otherwise the current or preferred account is selected.
6. Enable `IsPasswordEncryptionEnabled` if the password must be encrypted before session creation.

The connector uses Capital.com's documented live and demo REST hosts. The WebSocket URL is derived from the authenticated session response and completed with the documented `/connect` path.

## Market-data details

The public quote stream contains bid, offer and their quantities. It does not contain exchange trades or an order-book ladder, so the connector deliberately does not advertise tick trades or market depth. It does not fabricate either data type from quotes.

REST candles include bid and ask OHLC values; the connector exposes their midpoint. The WebSocket OHLC protocol identifies the price side in `priceType`; the connector uses the vendor's bid-side `classic` bar so that one native update produces one StockSharp candle.

Capital.com does not expose a private account/order WebSocket destination in the public protocol. When a live order-status or portfolio subscription is requested, the connector refreshes the corresponding typed REST snapshots at bounded intervals. No polling runs for those data types without an active subscription.

## Limits and access

- Capital.com documents a maximum REST rate of 10 requests per second per user; the connector serializes and spaces REST requests below that limit.
- `POST /session` is limited to one request per second.
- REST and WebSocket sessions require activity within 10 minutes; the connector sends the official REST and WebSocket pings every five minutes.
- A WebSocket connection supports at most 40 distinct EPICs. The connector enforces this before sending a subscription.
- Demo position and working-order creation is limited by Capital.com to 1,000 requests per hour.
- Product availability, leverage, guaranteed stops, real-stock restrictions and market-data permissions depend on the account, instrument and region.

## Official documentation

- [Capital.com Public API reference](https://open-api.capital.com/)
- [Capital.com API page](https://capital.com/trading-platforms/api)
- [Official Postman collection](https://github.com/capital-com-sv/capital-api-postman)
- [WebSocket API section](https://open-api.capital.com/#section/WebSocket-API)

Capital.com and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Capital.com.
