# QFEX connector for StockSharp

The connector integrates the current official QFEX production APIs for
perpetual futures on equities, indices, commodities, and currencies.

Supported functionality:

- active-instrument discovery through the official REST reference-data API;
- live Level1, complete Level2 snapshots, public trades, mark and underlier
  prices, open interest, and price limits through the public WebSocket;
- historical and streaming 1-minute, 5-minute, 15-minute, 1-hour, 4-hour, and
  daily candles;
- optional HMAC-authenticated balances, positions, open and historical orders,
  fills, and user trades;
- limit, market, add-liquidity-only, IOC, FOK, cancel, replace, and cancel-all
  order flows.

Public market data requires no credentials. Configure `Key` and `Secret`
together to enable account data and order entry. `AccountId` is optional and
selects a QFEX subaccount for both REST and WebSocket requests. Secrets are
used only to compute HMAC-SHA256 signatures and are never transmitted.

QFEX publishes complete pulsed order-book snapshots rather than incremental updates, so the connector advertises snapshot depth. The API does not expose public historical trades; tick subscriptions are therefore live-only.

Official resources:

- [QFEX API documentation](https://docs.qfex.com/)
- [QFEX WebSocket overview](https://docs.qfex.com/websocket/main)
- [QFEX OpenAPI specification](https://docs.qfex.com/api-reference/openapi.yaml)
- [QFEX market-data AsyncAPI specification](https://docs.qfex.com/websocket/mds.yaml)
- [QFEX trade AsyncAPI specification](https://docs.qfex.com/websocket/trade.yaml)
- [Official QFEX CLI](https://github.com/QFEX-org/cli)
- [StockSharp QFEX connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/qfex.html)

QFEX and its marks are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by QFEX.
