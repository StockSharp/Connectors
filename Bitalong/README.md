# Bitalong Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Bitalong connector** connects StockSharp to a legacy digital-asset exchange integration. It translates provider-specific data and operations into the unified StockSharp message model, so applications can use the same subscriptions and workflows across different venues.

The upstream service may no longer be available. This integration is retained for compatibility, maintenance of existing systems, and study of a complete connector implementation.

## Key capabilities

- Typical coverage: digital assets, spot markets.
- Instrument discovery and provider reference data.
- Market data supported by the adapter: Level 1 quotes, tick trades and order books.
- Provider-supported order submission and execution workflows.
- Portfolio, balance, position, and execution-state updates.
- Real-time subscriptions through the provider's streaming transport.
- Provider-specific transport, sessions, and data formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector to support an existing integration or as practical source code for learning how market data, transactions, and protocol details are mapped into StockSharp.

Before operational use, verify that the upstream API and required endpoints are still available.
