# StockSharp Tardis.dev Connector

The connector integrates StockSharp with the Tardis.dev instrument metadata
API and the official open-source Tardis Machine. It provides one consistent
adapter for normalized historical replay and real-time exchange streams. It is
a market-data connector and does not submit orders.

## Architecture and access

`Token` is a Tardis.dev Pro or Business API key. The connector sends it only in
the documented `Authorization: Bearer` header to the HTTPS cloud metadata API.
`Exchange` is one exact Tardis exchange ID, such as `binance`,
`binance-futures`, `deribit`, or `bitmex`. At connection time the adapter loads
`/v1/instruments/{exchange}`, including inactive and expired instruments, tick
sizes, amount increments, contract multipliers, currencies, strikes, option
types, and collection availability ranges. Reused exchange symbols retain the
full Tardis metadata identity in the native StockSharp security ID.

Normalized replay and live data are served by a locally running Tardis Machine.
Its default endpoints are `http://localhost:8000/` and
`ws://localhost:8001/`. Start the official Docker image or npm package with its
own `TM_API_KEY`/`--api-key`; that credential remains inside Tardis Machine and
is not placed in connector URLs. Historical replay is then downloaded and
cached by the machine, while real-time normalized streams connect directly to
the selected exchange. Exchange-specific credentials required by restricted
live channels are also configured on Tardis Machine, as documented upstream.

## Market data

The connector supports:

- security lookup from the typed Tardis instrument metadata API;
- tick-by-tick trades from normalized `trade` messages;
- L2 snapshots and incremental changes from `book_change` messages;
- best bid/ask plus derivative last, index, mark, and open-interest values as
  StockSharp Level 1 data;
- time-based trade bars at 1, 5, 10, and 30 seconds; 1, 5, 15, and 30 minutes;
  1 and 4 hours; and 1 day.

Historical subscriptions use the streaming HTTP `replay-normalized` endpoint;
the response is parsed line by line with a bounded NDJSON line size, rather
than buffered as one response. Live subscriptions use
`ws-stream-normalized`. Identical symbol/data-type subscriptions share one
WebSocket. A finite StockSharp `Count` closes a live subscription after the
requested number of emitted messages.

`HistoryLimit` caps emitted replay messages. `HistoryLookback` supplies a range
when no start is given, and `MaximumReplaySpan` prevents accidentally requesting
an unbounded tick-level backfill. Tardis `quote` reconstruction needs the
initial book state from midnight UTC, so Level 1 replay starts internally at
that boundary and filters pre-request output. Exchange and local timestamps are
parsed as UTC; the exchange timestamp is preferred. Order-book zero amounts are
preserved only for incremental removals. Trade bars are validated against the
requested interval before finished StockSharp candles are emitted.

Funding rates and predicted funding rates have no exact StockSharp Level 1
field and are therefore not mislabeled as another metric. Raw exchange-native
feeds, downloadable CSV datasets, liquidations, and option-summary greeks are
also left out because this adapter consumes the documented normalized types
with exact StockSharp equivalents. Every JSON request and response uses a
concrete DTO; there are no dynamic JSON trees, protocol dictionaries, anonymous
protocol objects, or untyped object arrays.

## Official documentation

- [Tardis.dev documentation](https://docs.tardis.dev/)
- [Instruments metadata API](https://docs.tardis.dev/api/instruments-metadata-api)
- [Cloud HTTP API](https://docs.tardis.dev/api/http-api-reference)
- [Tardis Machine quickstart](https://docs.tardis.dev/tardis-machine/quickstart)
- [Historical normalized replay](https://docs.tardis.dev/tardis-machine/replaying-historical-data)
- [Real-time normalized streaming](https://docs.tardis.dev/tardis-machine/streaming-real-time-data)
- [Normalized data types](https://docs.tardis.dev/tardis-machine/data-types)
- [Downloadable CSV datasets](https://docs.tardis.dev/downloadable-csv-files/overview)
- [Official Tardis Machine repository](https://github.com/tardis-dev/tardis-machine)
- [Tardis.dev website](https://tardis.dev/)
