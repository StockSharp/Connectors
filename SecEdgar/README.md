# SEC EDGAR Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **SEC EDGAR connector** gives StockSharp read-only access to official United States Securities and Exchange Commission filing data. It maps issuers, filings, and XBRL company facts to StockSharp securities, news, and a dedicated fundamental-data message type.

## Key capabilities

- Company discovery by ticker or CIK using the SEC company-ticker catalogue.
- Filing retrieval as StockSharp news, including recent submissions and a configurable number of historical submission files.
- Configurable filing-form filters such as 10-K, 10-Q, 8-K, 20-F, 40-F, and 6-K.
- XBRL company facts with date and record-count filters through the dedicated Company Facts data type.
- Finite REST requests suitable for historical collection and periodic refresh; the adapter does not open a push stream.
- No API key is required, but SEC policy requires an identifying User-Agent and respectful request pacing.
- No price quotes, trades, order books, candles, portfolios, or order-entry operations are provided.
- SEC endpoints, CIK handling, pagination files, and response formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for filing monitors, fundamental-research pipelines, issuer screening, and datasets that combine SEC disclosures with market data from another connector.

Coverage and timeliness depend on data published by the SEC. Request pacing, historical-file limits, fact limits, and filing-form filters are controlled by the adapter settings and SEC access policy.
