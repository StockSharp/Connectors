# Public.com Connector for StockSharp

This directory contains the Public.com API connector for the [StockSharp](https://github.com/StockSharp/StockSharp) trading platform. It uses the official Public Investing API for authentication, brokerage operations, reference data, quotes, option chains, and historical bars.

## Features

- US equities, ETFs, equity options, indexes, cryptocurrency, corporate bonds, and US Treasuries exposed by Public.com.
- Symbol lookup and option-chain discovery.
- Polling Level 1 quotes with bid, ask, last trade, volume, open interest, daily change, and option Greeks.
- Historical one-minute through monthly candles, including regular, extended, and overnight sessions supported by the API.
- Accounts, balances, positions, open orders, and incremental fill reporting.
- Market, limit, stop, and stop-limit orders.
- Native two-to-six-leg option orders through `PublicOrderCondition.Legs`.
- Order replacement and cancellation.
- Automatic renewal of the short-lived bearer token generated from the configured API secret.

Public.com does not publish a WebSocket endpoint for this API. The official SDK implements subscriptions through REST polling as well, so the connector groups quote requests and refreshes quotes, positions, and orders at `PollingInterval` without using an undocumented stream.

## Configuration

- `Token` — the personal API secret generated under **Account Settings → Security → API** in Public.com.
- `PollingInterval` — quote and brokerage snapshot refresh interval; the default is two seconds.

The connector requests a 15-minute access token and renews it five minutes before expiration. Public.com currently accepts `DAY` and `GTD` orders; a `TillDate` value selects `GTD` and must be no more than 90 days in the future.

## Documentation

- [StockSharp Public.com connector](https://doc.stocksharp.com/en/topics/api/connectors/stock_market/public.html)
- [Official Public Investing API](https://public.com/api)
- [Official API documentation](https://public.com/api/docs)
- [Official quickstart](https://public.com/api/docs/quickstart)
- [Official Python SDK](https://github.com/PublicDotCom/publicdotcom-py)
