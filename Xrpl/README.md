# XRPL Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **XRPL connector** connects StockSharp to the decentralized exchange built into the XRP Ledger. It maps configured currency pairs, ledger order books, executed offers, account balances, and signed transactions to StockSharp messages.

## Key capabilities

- Instrument discovery for configured XRP and issued-token pairs, including optional permissioned-DEX domain selection.
- Level 1 quotes and configurable-depth order-book snapshots with ongoing ledger updates.
- Historical and live tick trades derived from book changes, with time-frame candles built from ledger activity.
- Limit and price-protected IOC market offers, plus individual cancellation, replacement, and tracked group cancellation.
- Account balances, open offers, order states, fills, fees, and transaction-status updates.
- Public market data needs only RPC and WebSocket endpoints; trading requires a classic account address and family seed.
- Historical collection is bounded by the configured ledger scan limit, and live snapshots use the configured polling interval.
- XRPL amounts, issuers, signing, ledger sequences, fees, and event formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for XRPL DEX terminals, ledger-market analytics, historical studies, account monitoring, and direct offer execution.

Pair coverage, order-book liquidity, retained ledger history, transaction costs, finality, permissioned-domain access, and endpoint availability depend on XRPL network state and the configured service.
