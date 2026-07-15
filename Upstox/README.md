# Upstox V3 Connector for StockSharp

This directory contains the Upstox connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the current Upstox REST APIs, Market Data Feed V3 protobuf protocol, and Portfolio Stream WebSocket.

## Features

- Complete BOD instrument lookup for NSE, BSE, MCX, currency, index, futures, and options segments.
- Real-time Level 1, trades, five-level market depth, option greeks, open interest, and market status over Market Data Feed V3.
- Historical candles through the V3 flexible-interval API.
- Profile, funds, positions, holdings, orders, and trades.
- Real-time order, position, and holding updates through Portfolio Stream Feed.
- V3 order placement, modification, cancellation, automatic slicing, AMO, disclosed quantity, stop triggers, and market protection.
- Live and order-sandbox environments.

## Configuration

- `Token` — Upstox OAuth access token. A standard token is required for trading; an analytics token can be used for supported read-only and market-data operations.
- `IsDemo` — route place, modify, and cancel operations to the Upstox V3 sandbox. Upstox currently does not provide sandbox market-data or portfolio streams.
- `DefaultProduct` — default Delivery (`D`), Intraday (`I`), or MTF product. `UpstoxOrderCondition.Product` can override it per order.

Upstox identifies securities by `instrument_key`, for example `NSE_EQ|INE669E01016`. The connector stores this value in `SecurityId.Native`; applications should select instruments through the connector's security lookup before subscribing or trading.

## Documentation

- [StockSharp Upstox connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/upstox.html)
- [Upstox Developer API](https://upstox.com/developer/api-documentation/)
- [Market Data Feed V3](https://upstox.com/developer/api-documentation/v3/get-market-data-feed/)
- [Portfolio Stream Feed](https://upstox.com/developer/api-documentation/get-portfolio-stream-feed/)
- [Order API V3](https://upstox.com/developer/api-documentation/v3/place-order/)
- [Historical Candle Data V3](https://upstox.com/developer/api-documentation/v3/get-historical-candle-data/)
- [Authentication](https://upstox.com/developer/api-documentation/authentication/)
