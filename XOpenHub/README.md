# X Open Hub xAPI connector for StockSharp

This connector integrates StockSharp with the current X Open Hub xAPI 2.5 protocol. It uses the official `ws.xapi.pro` WebSocket endpoints operated for X Open Hub clients. It does not use the discontinued XTB retail API hosts.

## Supported functionality

- Demo and real X Open Hub environments.
- Login/password authentication and session-bound streaming authentication.
- Complete symbol lookup with instrument class, currencies, price precision, contract size, and volume limits.
- Native real-time Level 1 bid/ask, available size, session high, and session low.
- Historical candles for all documented periods from one minute through one month.
- Native one-minute candle streaming.
- Account balance, credit, equity, used margin, free margin, and margin level.
- Open positions, pending orders, trade history, native trade updates, and native transaction-status updates.
- Market orders, buy/sell limit orders, buy/sell stop orders, pending-order modification and cancellation.
- Full or partial position close through `XOpenHubOrderCondition.IsWithdraw` and `PositionId`.
- Stop loss, take profit, trailing offset, expiration, and custom comments.
- Automatic restoration of streaming subscriptions after a reconnect.

## Connection model

xAPI uses two independent persistent WebSocket connections:

- `wss://ws.xapi.pro/demo` or `wss://ws.xapi.pro/real` for request/reply commands;
- `wss://ws.xapi.pro/demoStream` or `wss://ws.xapi.pro/realStream` for live data.

The command socket performs login and returns `streamSessionId`. The streaming socket sends that identifier with each subscription. The connector subscribes to the documented keep-alive stream, observes the 200 ms command interval, caps outgoing protocol commands at 1 KiB, and serializes command requests so responses cannot be paired with the wrong caller.

An accepted command response from `tradeTransaction` only means that the server started processing the request. The connector therefore checks `tradeTransactionStatus` and treats only native status `ACCEPTED` as success.

## Configuration

- `Login` - X Open Hub user identifier.
- `Password` - account password.
- `IsDemo` - selects the demo endpoints when enabled and real endpoints when disabled.

Use the login value as `PortfolioName`, or leave the portfolio name empty. Instrument availability, leverage, market hours, and trading permissions are controlled by the X Open Hub account.

## Candle details

Historical chart prices use the protocol's integer-plus-delta representation and are converted with the returned precision. Streaming candle prices are already absolute values. The xAPI stream publishes only native one-minute candles; longer periods are available as historical subscriptions and are not advertised as fabricated real-time bars.

## Official documentation

- [X Open Hub xAPI product page](https://xopenhub.pro/api/)
- [Official xAPI protocol documentation, version 2.5.0](https://xopenhub.pro/api/xapi-protocol-documentation/)
- [StockSharp X Open Hub connector](https://doc.stocksharp.com/en/topics/api/connectors/forex/xopenhub.html)

X Open Hub and its logo are trademarks of their respective owner. StockSharp is not affiliated with or endorsed by X Open Hub.
