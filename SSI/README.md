# SSI Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **SSI connector** connects StockSharp to SSI FastConnect API v3 for the Vietnamese securities market. It translates SSI market data and brokerage operations into the unified StockSharp message model.

## Key capabilities

- Security and index discovery for HOSE, HNX, and UPCOM, including stocks and supported futures.
- Real-time Level 1 quotes, tick trades, and order-book subscriptions, with initial REST snapshots where available.
- Historical time-frame candles followed by streaming candle updates for supported intervals.
- Submission, replacement, and cancellation of individual orders, including SSI-specific order conditions.
- Account discovery plus balance, position, order, and execution updates through streaming and periodic reconciliation.
- Configurable REST and WebSocket endpoints and a portfolio polling interval.
- FastConnect credentials are required; trading additionally depends on the configured client ID, account, RSA private key, and current OTP.
- SSI-specific sessions, payloads, and stream topics are hidden behind the standard StockSharp API.

## Typical use

Use this connector for Vietnamese-market trading terminals, live strategies, order-management services, and monitoring tools that need direct SSI brokerage access.

Available instruments, historical depth, trading permissions, request limits, and service availability are controlled by SSI and the connected FastConnect account.
