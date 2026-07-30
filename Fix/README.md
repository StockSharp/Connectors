# FIX Protocol Connector

The **FIX Protocol connector** connects StockSharp to brokers, exchanges, and trading systems through configurable Financial Information eXchange sessions. It maps dialect-specific messages to the unified StockSharp message model.

## Key capabilities

- Configurable FIX dialects for multiple brokers, venues, and market segments.
- Session logon, authentication, heartbeats, sequence tracking, resend handling, reconnection, and optional secure transport.
- Instrument discovery and market data such as Level 1 quotes, trades, order books, candles, news, and order-log events when supported by the dialect.
- Order submission, replacement, cancellation, mass cancellation, status, and execution workflows when supported by the counterparty.
- Portfolio, balance, and position updates for transactional sessions.
- Sender, target, account, endpoint, and session settings exposed through the standard StockSharp configuration model.

## Typical use

Use this connector for custom broker integrations, exchange gateways, live strategies, order-management services, and normalized market-data access.

Exact messages, fields, order types, recovery behavior, and permissions depend on the selected FIX dialect and the counterparty's session specification.
