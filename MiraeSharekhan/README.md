# StockSharp Mirae Asset Sharekhan connector

StockSharp adapter for the current Mirae Asset Sharekhan Trading API. It uses the official
REST service for scrip masters, historical charts, orders, reports, trades/positions,
holdings, and funds, plus the official WebSocket endpoint for live prices and top-five
market depth.

## Credentials

Create an application in the Mirae Asset Sharekhan Trading API portal and complete the
official browser login flow. Configure the adapter with the resulting `API key`, `Access
token`, and trading `Customer ID`. `Vendor key` is needed only for a vendor application.
The access token is accepted as an input deliberately: interactive login requires a user
redirect and must not be simulated by a headless connector.

## Supported operations

- scrip-master lookup for NSE/BSE cash, derivatives, currency, and MCX;
- historical 1, 5, 15, 30, and 60 minute candles and daily candles;
- live LTP/Level1, trades when the feed supplies last quantity, and top-five depth;
- new, modify, and cancel order requests with typed Sharekhan order conditions;
- day order/trade reports, holdings, funds, and position snapshots;
- automatic WebSocket reconnect and restoration of market-data subscriptions.

Use the native Sharekhan exchange codes (`NC`, `BC`, `NF`, `BF`, `RN`, `RB`, `MX`) in a
security board code. A security returned by lookup also carries its Sharekhan scrip code in
`SecurityId.Native`, which avoids symbol ambiguity when subscribing or placing orders.

## Protocol notes

Mirae Asset Sharekhan currently documents a maximum of 1000 symbols per WebSocket
connection. Historical intraday availability is limited by the broker, and historical data
is not corporate-action adjusted. The connector sends and receives only typed protocol
DTOs; it does not use dynamic JSON objects or protocol dictionaries.

The official .NET SDK exposes textual WebSocket messages. The official Python SDK contains
an empty binary-frame decoder, so no public binary layout is available to implement safely.
This connector accepts official text frames and binary frames containing UTF-8 JSON, and
reports a clear protocol error if the server sends an undocumented binary payload.

## Official documentation

- [Trading API documentation](https://www.sharekhan.com/trading-api/documentation/overview)
- [Trading API FAQ](https://www.sharekhan.com/trading-api/faq)
- [Official Python SDK](https://github.com/Sharekhan-API/shareconnectpython)
- [Official .NET SDK](https://github.com/Sharekhan-API/shareconnectcsharp)
