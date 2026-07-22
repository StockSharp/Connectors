# ProBit Global Connector

The connector integrates ProBit Global spot markets through the official REST,
OAuth 2.0, and WebSocket APIs.

Supported features:

- spot-market discovery with price and quantity increments;
- Level1 quotes, order-book snapshots, public trades, and time-frame candles;
- realtime ticker, order-book, trade, and candle updates;
- balances, open orders, order history, and account trade history;
- realtime private balance, order, and account trade updates;
- limit and market orders with GTC or IOC execution;
- individual cancellation and filtered group cancellation.

Public market data does not require credentials. Trading and private data use
the OAuth client ID and client secret configured through `Key` and `Secret`.
A market buy requires its quote-currency amount in
`ProBitOrderCondition.QuoteAmount`; other orders use the regular StockSharp
volume.

Official resources:

- [ProBit Global API documentation](https://docs-en.probit.com/)
- [ProBit Global API credentials](https://www.probit.com/en-us/my-page/api-management/api-credential)
