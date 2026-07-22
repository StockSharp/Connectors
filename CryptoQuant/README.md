# StockSharp CryptoQuant Connector

The connector integrates StockSharp with the CryptoQuant API v1. It is a
historical market-data connector for CryptoQuant's multi-chain USD price
indices. It does not submit orders. The current customer API documentation
describes REST endpoints and does not publish a client WebSocket contract, so
the adapter does not advertise streaming, ticks, order books, or live Level 1
data.

## Access and configuration

CryptoQuant API access tokens are available with eligible paid plans. Configure
the token in `Token`; the connector sends it only in the documented
`Authorization: Bearer` header. `ApiEndpoint` defaults to
`https://api.cryptoquant.com/v1/` and accepts only an HTTPS API-v1 root without
embedded credentials, query parameters, or fragments.

CryptoQuant does not publish one universal numeric request limit for every
account in the customer documentation. The connector therefore uses a
conservative one-second `RequestInterval` by default. Rate-limit and transient
server responses use bounded retries and honor `Retry-After` when supplied.
Response bodies are size-limited, and access tokens are redacted from surfaced
transport errors.

At connection time the adapter calls `/discovery/endpoints` and selects only
documented `/v1/{namespace}/market-data/price-ohlcv` routes. Native index routes
such as BTC, ETH, and XRP become one security per namespace. Routes that
advertise a `token` parameter, including stablecoin, ERC-20, and altcoin data,
become one security per discovered token. The connector does not invent
exchange, market, or entity combinations that discovery does not advertise.
Each security retains its namespace-and-token discovery key as its native
identity on the `CRYPTOQUANT` board.

## Market data

The connector supports:

- discovery-driven CryptoQuant price-index lookup;
- historical USD closing prices as StockSharp Level 1 data;
- historical USD OHLCV candles at one-minute, one-hour, and daily windows.

The adapter checks each requested window against the values advertised for its
discovery endpoint. `PriceTimeFrame` selects the window used for Level 1
downloads. `HistoryLimit` caps records per subscription, while
`HistoryLookback` supplies the range only when a request has no start time.
Explicit long ranges are bounded before download so one request cannot silently
exceed the configured record cap.

CryptoQuant responses are normalized to UTC, sorted chronologically, and deduplicated by opening time before StockSharp messages are emitted. Positive OHLC values and candle consistency are validated. A missing API volume remains zero in the StockSharp candle instead of being inferred from another metric.

## Official documentation

- [CryptoQuant API introduction](https://userguide.cryptoquant.com/api/introduction)
- [Authentication](https://userguide.cryptoquant.com/api/authentication)
- [Time convention](https://userguide.cryptoquant.com/api/time-convention)
- [Endpoint discovery](https://userguide.cryptoquant.com/api/available-endpoints)
- [Bitcoin market data](https://userguide.cryptoquant.com/api/btc-market-data)
- [Ethereum market data](https://userguide.cryptoquant.com/api/eth-market-data)
- [XRP market data](https://userguide.cryptoquant.com/api/xrp-market-data)
- [Stablecoin market data](https://userguide.cryptoquant.com/api/stablecoin-market-data)
- [ERC-20 market data](https://userguide.cryptoquant.com/api/erc20-market-data)
- [Altcoin market data](https://userguide.cryptoquant.com/api/alt-market-data)
- [Response status and errors](https://userguide.cryptoquant.com/api/introduction/status-and-error-codes)
- [CryptoQuant institutional API](https://cryptoquant.com/institutional-api)
