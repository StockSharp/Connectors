# BTC Markets Connector

The connector integrates BTC Markets cryptocurrency spot markets through the
current REST v3 API and the official WebSocket v2 feed.

Supported features:

- discovery of all markets with their native price and volume precision;
- Level1 data, full order books, public trades, and OHLCV candle history;
- realtime Level1, trades, and incremental order books through WebSocket;
- CRC32 order-book checksum validation with automatic snapshot recovery;
- balances, current and historical orders, and account trades;
- realtime private order and fund changes through authenticated WebSocket;
- market, limit, stop, stop-limit, and take-profit orders;
- GTC, IOC, FOK, post-only, self-trade prevention, order replacement, and
  individual or filtered bulk cancellation.

API credentials are optional for public market data. Trading and account data
require an API key and its Base64-encoded private key. Grant only the account
permissions needed by the application.

Every REST and WebSocket payload is represented by a concrete DTO. The
transport does not use dynamic JSON trees, anonymous protocol objects, or
protocol dictionaries.

Official documentation:

- [BTC Markets API documentation](https://docs.btcmarkets.net/)
- [BTC Markets REST API v3](https://docs.btcmarkets.net/doc/)
- [Official BTC Markets API clients](https://github.com/BTCMarkets)
