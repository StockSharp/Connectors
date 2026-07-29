# Transaq Connector

The **Transaq connector** connects StockSharp to a Transaq trading server used by Russian brokers. It converts the server's XML commands and asynchronous updates into the unified StockSharp message model.

## Key capabilities

- Security, board, market, and supported-data discovery for equities, futures, and options.
- Real-time Level 1 quotes, tick trades, incremental order books, candles, and news.
- Historical tick and candle requests supported by the server.
- Standard, conditional, stop, repo, and negotiated-order workflows, including replacement and cancellation.
- Portfolio, limits, leverage, cash, positions, orders, and own-trade updates.
- Production and demo endpoints, proxy support, password changes, heartbeat, and serialized command processing.

## Typical use

Use this connector for broker terminals, live Russian-market strategies, order management, account monitoring, charting, and historical analysis.

Available instruments, history, order types, account fields, and trading permissions depend on the Transaq server, broker configuration, and connected account.
