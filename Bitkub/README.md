# Bitkub Connector

The connector provides access to Bitkub spot markets through the current REST v3/v4 APIs and public and private WebSocket APIs.

Supported features:

- security lookup;
- Level1 market data, market depth, and public trades;
- recent REST snapshots and live public WebSocket updates;
- portfolio balances through Wallet API v4;
- market and limit order registration, cancellation, and order status updates;
- authenticated private WebSocket order and match notifications.

Market-buy orders on Bitkub are submitted as a quote-currency spending amount. Set `BitkubOrderCondition.QuoteAmount` explicitly for those orders. Limit-buy spending amounts are calculated from the requested base volume and price.

API credentials are optional for public market data and required for portfolio and transaction operations.

Official API documentation: [bitkub-official-api-docs](https://github.com/bitkub/bitkub-official-api-docs).
