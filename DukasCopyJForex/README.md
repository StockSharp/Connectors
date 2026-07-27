# Dukascopy JForex Connector

[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **Dukascopy JForex connector** connects StockSharp to Dukascopy Bank through the official Java JForex SDK. The SDK establishes the secure authenticated session with Dukascopy trading servers; the .NET adapter exchanges commands and events with it through a loopback-only bridge.

## Key capabilities

- Discovery of FX, CFD, metal, index, commodity, bond, and other instruments exposed by the account.
- Level 1 quotes, tick trades, order-book updates, and time-frame candles.
- Historical tick and candle requests through JForex history services.
- Market, limit, stop, stop-limit, and JForex-specific order commands.
- Order registration, replacement, cancellation, execution updates, balances, and positions.
- Separate demo and live JForex service addresses, both configurable in adapter settings.
- The bridge can be started from a supplied executable JAR or managed as a separate local process.

## Runtime model

Java is required because Dukascopy publishes and supports JForex as a Java API. The included Maven bridge project uses the official `DDS2-jClient-JForex` package. It listens only on the local loopback interface and does not expose account credentials to the network.

Use the connector for live strategies, terminals, monitoring, and order-management services that need Dukascopy through the standard StockSharp message model. Available instruments, history, market depth, and trading permissions depend on the connected Dukascopy account.
