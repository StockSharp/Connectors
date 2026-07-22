# StockSharp Osmosis Connector

The connector integrates Osmosis mainnet through the official Smart Query
Server (SQS), Cosmos LCD, CometBFT RPC and WebSocket endpoints.

## Supported operations

- configured denomination-pair validation against the official Osmosis asset
  list;
- best-route exact-input and exact-output quotes through SQS;
- executable Level1 bid and ask probes;
- real-time direct-hop swaps from the CometBFT `token_swapped` WebSocket
  stream;
- wallet balances for every token used by a configured market;
- immediate exact-input sell and exact-output buy swaps;
- local secp256k1 `SIGN_MODE_DIRECT` transaction signing;
- transaction simulation, dynamic EIP base-fee calculation, broadcast and
  receipt tracking;
- current connector-session order and execution status.

`Markets` contains semicolon-separated
`base-denom|quote-denom` entries. An optional third field overrides the
StockSharp security code:

```text
uosmo|ibc/498A0751C798A0D9A389AA3691123DADA57DAA4FE165D5C75894505B876BA6E4
```

The default list contains OSMO/USDC, ATOM/USDC, BTC/USDC and ETH/USDC using
the current canonical denominations from the official asset list. Explicit
denominations are never silently replaced by similarly named variants.

Public market data does not require a wallet. A public `osmo` address is
required for balances, and the matching raw 32-byte hexadecimal private key
is required only for trading. The key remains local. Before broadcasting, the
connector obtains a protected SQS route, simulates the transaction, applies
the configured gas adjustment, reloads the current base fee and verifies the
available input-token and OSMO gas balances.

## Market-data boundary

Osmosis is an AMM and has no central-limit order book. The connector therefore
does not invent market depth. Its Level1 values are executable SQS route
probes, not a synthetic order book.

The official CometBFT WebSocket emits one `token_swapped` event per route hop.
The connector publishes an accurate tick only when that individual hop
directly matches a configured denomination pair. It does not collapse
multi-hop events into fabricated trades. Public endpoints do not promise a
stable archive service, so historical ticks and candles are deliberately not
advertised. Indexed history can be added separately through a provider with
an explicit archive contract.

## Official documentation

- [Osmosis documentation](https://docs.osmosis.zone/)
- [Official endpoints](https://docs.osmosis.zone/integrate/endpoints/)
- [Smart Query Server](https://docs.osmosis.zone/integrate/endpoints/sqs/)
- [Swap integration](https://docs.osmosis.zone/integrate/swap/)
- [Transaction fees](https://docs.osmosis.zone/integrate/fees/)
- [Transaction structure](https://docs.osmosis.zone/integrate/transaction-structure/)
- [Official asset lists](https://github.com/osmosis-labs/assetlists)
- [Osmosis protocol source](https://github.com/osmosis-labs/osmosis)
