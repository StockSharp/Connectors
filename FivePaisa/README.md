# 5paisa Xstream connector

This directory contains the 5paisa Xstream connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform.

## Protocol coverage

- The official Xstream REST API for the scrip master, order placement, modification and cancellation, order and trade books, margin, holdings, positions, and historical candles.
- The official routed market WebSocket (`openfeed`, `aopenfeed`, or `bopenfeed`, selected from the access-token payload) for realtime Level1 updates, last trades, and account-wide order/trade confirmations.
- The separate official `20depth` WebSocket for 20-level NSE order books. The connector rejects depth requests for BSE and other unsupported exchanges before subscribing.
- Historical candles at 1, 5, 10, 15, 30, and 60 minutes and one day. Requests are split into the documented six-month windows. Xstream does not expose a realtime candle channel, so candle subscriptions must be history-only.
- Portfolio snapshots from margin, net-position, and holding endpoints, refreshed every 30 seconds while a live portfolio subscription is active.

All REST bodies, REST responses, WebSocket commands, market updates, depth updates, order confirmations, and candle rows use typed DTOs. Candle arrays are decoded by a dedicated typed JSON converter; the connector does not construct protocol payloads with `JObject`, `JArray`, dynamic objects, or protocol dictionaries.

## Authentication

Configure the app key, demat client code, and a current bearer access token issued through the 5paisa Xstream developer portal. The access token is normally valid for one trading day and must be renewed outside the connector. The connector does not store account credentials or perform the interactive login flow.

`AlgoId` defaults to zero for non-algorithmic orders. Accounts placing exchange-registered algorithmic orders must configure the identifier assigned for that algorithm.

## Security identifiers

Run a security lookup before subscribing or trading. The connector downloads the official complete scrip master and stores `exchange|exchangeType|scripCode` in `SecurityId.Native`. This preserves the exchange segment when numeric scrip codes overlap.

Supported mappings include NSE cash, derivatives and currency; BSE cash, derivatives and currency; and MCX derivatives. The dedicated 20-level feed is available only for NSE instruments and may require the corresponding market-data entitlement.

## Official references

- [5paisa Xstream developer documentation](https://xstream.5paisa.com/dev-docs)
- [Scrip master](https://xstream.5paisa.com/dev-docs/docFundamentals/scrip-master)
- [Market feed](https://xstream.5paisa.com/dev-docs/market-data-system/market-feed)
- [20-depth feed](https://xstream.5paisa.com/dev-docs/market-data-system/20MarketDepth)
- [Order and trade confirmations](https://xstream.5paisa.com/dev-docs/order-tracking-system/web-socket-trade)
- [Historical candles](https://xstream.5paisa.com/dev-docs/market-data-system/historical-candles)
- [Official .NET SDK](https://github.com/5paisa/5paisa-dotnet)
