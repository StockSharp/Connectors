# OpenFIGI Connector
[Русский](README_ru.md) | [中文](README_zh.md) | [Español](README_es.md) | [Deutsch](README_de.md) | [Português](README_pt.md) | [日本語](README_ja.md)

The **OpenFIGI connector** connects StockSharp to a financial-instrument identifier mapping and reference-data service. It translates provider-specific results into the unified StockSharp security model, so applications can use consistent instrument identifiers across different data sources.

## Key capabilities

- Typical coverage: global financial instruments and identifier metadata.
- Identifier mapping by FIGI, ISIN, CUSIP, SEDOL, ticker, or another OpenFIGI identifier type.
- Instrument search and filtering by exchange code, MIC, currency, market sector, and security type.
- Normalized StockSharp security messages with provider reference data and identifiers.
- This adapter is read-only: it does not provide price streams or route orders.
- Provider-specific REST transport, pagination, throttling, and response formats are hidden behind the standard StockSharp API.

## Typical use

Use this connector for security-master maintenance, identifier enrichment, cross-provider reconciliation, and onboarding instruments into StockSharp workflows.

Available mappings, search results, page sizes, rate limits, and service availability are controlled by OpenFIGI and by whether an API key is configured.
