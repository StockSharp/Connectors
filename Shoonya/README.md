# Shoonya API connector

This directory contains the Shoonya API connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- Official Noren REST endpoints for security masters, intraday and daily candles, orders, trades, limits, positions, and holdings.
- The official single WebSocket connection at wss://api.shoonya.com/NorenWSTP/ for touchline, five-level depth, and account-wide order and fill updates.
- NSE, BSE, NFO, BFO, CDS, and MCX security masters downloaded from the official daily ZIP files.
- Market, limit, stop-limit, stop-market, delivery, intraday, normal, cover, bracket, DAY, and IOC orders.

## Authentication

Configure the Shoonya user ID and the current susertoken returned by the official login flow. AccountId normally equals the user ID and defaults to it when left empty. The connector deliberately accepts an already-issued session token and does not store the account password, vendor secret, or TOTP seed.

Shoonya session tokens are short-lived and must be refreshed through the broker's supported login flow. Disconnecting the connector does not invalidate the externally managed token.

## Security identifiers and streaming

Run a security lookup before subscribing or trading. The connector stores exchange|token in SecurityId.Native, because numeric tokens overlap between exchange segments.

Shoonya permits one WebSocket connection per session. The connector shares that connection between market data and order updates and automatically restores active subscriptions after reconnecting. Sparse tf and df updates are merged with the latest acknowledged snapshot before StockSharp messages are emitted.

Intraday candles are available for 1, 3, 5, 10, 15, 30, 60, 120, and 240-minute intervals. Daily candles use the separate official EOD endpoint. The API does not document historical ticks or historical order books, so those are not advertised.

## Official references

- [Shoonya API documentation](https://shoonya.com/api-documentation)
- [Official Python SDK and protocol reference](https://github.com/Shoonya-Dev/ShoonyaApi-py)
- [Official endpoint list](https://faq.shoonya.com/api/what-are-the-endpoint-urls-used-in-shoonya-apis/)
- [Shoonya developer portal](https://shoonya.com/)
