# Quidax Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Quidax connector** integrates StockSharp with the Quidax spot exchange. It is especially useful for observing and trading crypto markets quoted in NGN and other African fiat currencies, as well as crypto-to-crypto pairs.

## Key capabilities

- Spot instrument discovery with currency composition, price and volume precision, and minimum order value.
- Level 1 quotes, Level 2 order books, public trades, and historical candles.
- Continuous market-data subscriptions through configurable REST polling.
- Wallet balances, open and historical orders, and private executions.
- Limit and market order registration, individual cancellation, and filtered bulk cancellation.
- Configurable REST service address, account or subaccount identifier, and polling interval.

Public market data is available without credentials. Portfolio and trading operations require a Quidax secret key. The default `me` user identifier targets the token owner and can be replaced with a supported subaccount identifier.
