# StockSharp Glassnode Connector

The connector integrates StockSharp with the Glassnode API v1. It is a
historical market-data connector for Glassnode's digital-asset catalogue and
composite USD price metrics. It does not submit orders and does not advertise
trades, order books, or streaming because those operations are not part of the
documented Glassnode REST contract used here.

## Access and configuration

Glassnode API access requires a Professional subscription with the API add-on.
Configure the generated API key in `Token`. The connector sends it only in the
documented `X-Api-Key` header, never in a URL. `ApiEndpoint` defaults to
`https://api.glassnode.com/v1/` and accepts only an HTTPS API-v1 root without
embedded credentials, query parameters, or fragments.

The default `RequestInterval` is 100 milliseconds, matching Glassnode's
documented standard limit of 600 requests per minute. Responses with rate-limit
or transient server status codes use bounded retries and honor `Retry-After` or
`x-rate-limit-reset` when supplied. Response bodies are size-limited and API
keys are redacted from surfaced transport errors.

At connection time the adapter loads `/metadata/assets`. Every security keeps
the canonical Glassnode asset ID in its native identity and is represented as
`asset/USD` on the `GLASSNODE` board. Lookup can match the ID, symbol, name,
default network, or semantic tags. Asset type and token-chain metadata are read
through concrete DTOs; external IDs are not used as unstable security keys.

## Market data

The connector supports:

- Glassnode asset discovery;
- historical composite USD closing prices as StockSharp Level 1 data;
- historical composite USD OHLC candles at 10-minute, one-hour, and daily
  intervals.

`PriceTimeFrame` selects the native interval used by Level 1 downloads.
`HistoryLimit` caps records per subscription, while `HistoryLookback` supplies
the range only when the request has no start time. Explicit long ranges are
bounded before download so one request cannot silently exceed the configured
record cap.

Glassnode timestamps are Unix seconds in UTC and denote the beginning of their
resolution interval. The connector validates timestamp bounds, positive OHLC
values, and candle consistency before emitting finished candles. Glassnode's
API has no generic volume field for the composite OHLC metric, so the adapter
does not invent candle volume.

Glassnode exposes many on-chain, derivatives, ETF, and other analytical
metrics whose values may be scalars, objects, or arrays. They are not coerced
into unrelated StockSharp price fields. This connector deliberately maps only
the standard price series with an exact StockSharp representation. Every
response it does consume has a concrete DTO; no dynamic JSON trees, anonymous
protocol objects, protocol dictionaries, or untyped object arrays are used.

## Official documentation

- [Glassnode API introduction](https://docs.glassnode.com/basic-api/api)
- [API-key authentication](https://docs.glassnode.com/basic-api/api-key)
- [API credits and rate limits](https://docs.glassnode.com/basic-api/api-credits)
- [Metadata endpoints](https://docs.glassnode.com/basic-api/metadata)
- [Market metric endpoints](https://docs.glassnode.com/basic-api/endpoints/market)
- [Timestamps and resolutions](https://docs.glassnode.com/data/general-information/timestamps-and-resolutions)
- [Supported assets](https://docs.glassnode.com/data/supported-assets)
- [Official Glassnode CLI](https://github.com/glassnode/glassnode-cli)
- [Glassnode website](https://glassnode.com/)
