# CoinJar Exchange Connector

The connector integrates CoinJar Exchange spot markets through the current
Trading REST API, Market Data REST API, and official Phoenix WebSocket feed.

Supported features:

- active-product discovery with native currency metadata and dynamic price
  levels;
- Level1, Level2 order books, public trades, and OHLCV candle history;
- realtime tickers, trades, and incremental order books through WebSocket;
- explicit order-book snapshots and automatic recovery after reconnects;
- cash accounts, open and historical orders, and fills;
- realtime private account, order, and fill events through WebSocket;
- market, limit, and stop-limit orders;
- GTC, IOC, maker-or-cancel (post-only), auction-only, individual cancellation,
  and filtered bulk cancellation.

An API token is optional for public market data. Trading, balances, and the
private WebSocket channel require a CoinJar Exchange API token. Grant only the
permissions needed by the application.

CoinJar uses price-dependent tick and minimum trade sizes. StockSharp security
metadata exposes the currency subunit as the baseline step, while every order
is validated against the exact exchange price level before it is submitted.

Every REST and WebSocket payload is represented by a concrete DTO. The
transport does not use dynamic JSON trees, anonymous protocol objects, or
protocol dictionaries.

Official documentation:

- [CoinJar Exchange API documentation](https://docs.exchange.coinjar.com/)
- [REST API reference](https://docs.exchange.coinjar.com/reference/)
- [WebSocket data-feed guide](https://docs.exchange.coinjar.com/reference/getting-started-data-feed)
