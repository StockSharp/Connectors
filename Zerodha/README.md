# StockSharp Zerodha connector

The connector integrates StockSharp with the official [Zerodha Kite Connect 3 API](https://kite.trade/docs/connect/v3/). It uses REST for instruments, historical candles, portfolio data and trading commands, and the native Kite WebSocket for binary market data and JSON order postbacks.

## Supported functionality

- Daily Kite login token exchange, or use of an already-issued access token.
- The complete daily instruments CSV for NSE, BSE, NFO, BFO, CDS, BCD and MCX products.
- Native real-time trades, Level1 and five-level bid/ask depth.
- Historical candles for 1, 3, 5, 10, 15, 30 and 60 minutes and one day, including open interest when supplied.
- Equity holdings, net positions, equity and commodity funds and margin figures.
- Market, limit, stop-limit and stop-market orders.
- CNC, NRML, MIS and CO products; regular, AMO, cover, iceberg and auction varieties.
- DAY, IOC and TTL validity, disclosed quantity, market protection and automatic order slicing.
- Order modification and cancellation.
- Current-day order and trade snapshots plus native real-time order postbacks over WebSocket.

## Authentication

1. Create a Kite Connect application and set `Key`.
2. Open `https://kite.zerodha.com/connect/login?v=3&api_key=YOUR_API_KEY` in a browser and complete Zerodha login and 2FA.
3. Copy the short-lived `request_token` returned to the application's registered redirect URL.
4. Set `RequestToken` and `Secret`. If `Token` is empty, the connector computes the documented SHA-256 checksum, exchanges the request token and stores the resulting access token in `Token`.

Alternatively, set an already-issued access token directly in `Token`; `Secret` and `RequestToken` are then not sent or required. Kite access tokens expire at 06:00 on the following day or earlier after logout/session invalidation. Kite Connect has no refresh-token flow for ordinary individual applications, so a new interactive login is required after expiry.

## Streaming and market data

The connector connects to `wss://ws.kite.trade` with `api_key` and `access_token`, restores subscriptions after reconnect and selects `quote` or `full` mode according to active StockSharp subscriptions. `full` packets carry the last trade timestamp, open interest and 5×5 depth. One connection supports at most 3,000 instrument tokens, and Zerodha permits at most three WebSocket connections per API key.

The one-byte WebSocket heartbeat is ignored as documented. Prices are decoded with the segment-specific official divisors, including CDS and BCD. Index packets use their separate 28/32-byte schema. Quote packets report the latest trade rather than a separate exchange trade channel; the connector emits a tick only when the native last-trade timestamp/price/quantity tuple changes.

Kite does not provide a native live-candle stream. Candle subscriptions return REST history and finish rather than fabricating bars from quotes. The instrument master is generated once per day, and its `last_price` is not treated as a live quote.

## Trading and portfolio details

- Use the connected Zerodha user id as `PortfolioName`; an empty portfolio name selects it automatically.
- Kite quantities are whole units. The connector rejects fractional order volume instead of rounding it silently.
- Order history in Kite is transient and covers the current trading day. Real-time updates come from WebSocket order postbacks.
- Kite WebSocket has no native holdings, positions or funds stream. While a live portfolio subscription exists, the connector refreshes the REST snapshot every 15 seconds; no portfolio polling runs without that subscription.
- Successful order placement only acknowledges receipt by Kite. The WebSocket postback or order snapshot determines final exchange state and fills.
- Product availability, order varieties and market permissions depend on the Zerodha account and exchange segment.

## Official limits

- Quotes: 1 REST request per second; historical candles: 3 requests per second; order placement and other endpoints: 10 requests per second.
- At most 10 orders per second, 400 orders per minute and 5,000 orders per day per user/API key.
- At most 25 modifications per order.
- Up to 3,000 instruments per WebSocket connection and up to three connections per API key.

The connector serializes REST calls and applies the documented historical/standard request spacing. It retries only idempotent GET requests on transient transport/server failures; order commands are not automatically replayed.

## Official documentation

- [Kite Connect 3 introduction](https://kite.trade/docs/connect/v3/)
- [Authentication and access tokens](https://kite.trade/docs/connect/v3/user/)
- [WebSocket binary streaming](https://kite.trade/docs/connect/v3/websocket/)
- [Market instruments and quotes](https://kite.trade/docs/connect/v3/market-data-and-instruments/)
- [Historical candles](https://kite.trade/docs/connect/v3/historical/)
- [Orders and trades](https://kite.trade/docs/connect/v3/orders/)
- [Portfolio](https://kite.trade/docs/connect/v3/portfolio/)
- [Errors and rate limits](https://kite.trade/docs/connect/v3/exceptions/)
- [Official client libraries](https://github.com/zerodha)

Zerodha, Kite and their logos are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by Zerodha.
