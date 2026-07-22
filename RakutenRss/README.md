# Rakuten MARKETSPEED II RSS Connector for StockSharp

This connector automates the official Rakuten Securities MARKETSPEED II RSS Excel add-in. It does not scrape the website or emulate an undocumented network service.

## Features

- Real-time Level1 quotes and ten-level order books for Japanese equities, futures, and options.
- Tick lists and live updates through the official Excel RSS formulas.
- Intraday, daily, weekly, and monthly candlestick data with all documented time frames.
- Explicit-code security lookup for Tokyo/JAX/JNX equities and Osaka derivatives.
- Cash equity, margin open/close, and futures/options open/close orders.
- Order amendment and cancellation.
- Equity and derivatives order lists, executions, cash and margin positions, derivatives positions, buying power, and margin information.
- A dedicated STA automation thread.

## Requirements and configuration

MARKETSPEED II RSS is a local Windows API exposed exclusively as an Excel add-in. Install a supported desktop Microsoft Excel version and MARKETSPEED II, register the RSS add-in, log in to MARKETSPEED II, and enable RSS ordering before connecting StockSharp.

- `PortfolioName` - local StockSharp portfolio name for the account currently logged in to MARKETSPEED II.
- `IsExcelVisible` - show the private Excel instance owned by the connector for diagnostics.
- `MaxTableRows` - maximum rows read from order, execution, position, and account tables.

The connector creates a private, unsaved workbook and closes the Excel instance on disconnect. Do not edit this workbook manually. MARKETSPEED II must remain logged in. Rakuten's order confirmation and per-order limit settings still apply; the trading PIN is configured in MARKETSPEED II and is never handled by the connector.

## Behavior and limitations

MARKETSPEED II RSS has no complete instrument-master enumeration function. Security lookup therefore requires an explicit code. Equity codes use the `.T`, `.JAX`, or `.JNX` suffix; nine-character Osaka futures/options codes use board `OSE`.

The add-in pushes data into Excel cells. The connector reads those live cells on its heartbeat and emits messages only when the value snapshot changes. This preserves the official realtime source without inventing a WebSocket or polling Rakuten's website.

Execution lists do not expose the originating order number. Executions are therefore published with stable signatures derived from their documented fields but without a fabricated order link. Margin and derivative closing orders require the opening date and opening price in `RakutenRssOrderCondition`, matching the native RSS functions.

The native derivatives amendment function changes price and validity but not quantity. Historical intraday candles are limited to the current chart window supplied by `RssChart`; `RssChartPast` provides dated history only for daily, weekly, and monthly bars.

## Documentation

- [StockSharp Rakuten RSS connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/rakuten_rss.html)
- [Official MARKETSPEED II RSS site](https://marketspeed.jp/ms2_rss/)
- [Official online help](https://marketspeed.jp/ms2_rss/onlinehelp/)
- [Official function reference](https://marketspeed.jp/guide/manual/ms2rss_function.pdf)
- [Official order functions](https://marketspeed.jp/ms2_rss/onlinehelp/ohm_002/ohm_002_06.html)
- [Official system requirements](https://marketspeed.jp/ms2_rss/system_requirements/)
