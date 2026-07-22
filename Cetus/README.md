# StockSharp Cetus Connector

The connector integrates Cetus CLMM on Sui mainnet through the official
Cetus Router API and the supported Sui Full Node gRPC v2 protocol.

## Supported operations

- configured-pool validation against live Sui objects and their concrete coin
  types;
- coin metadata and wallet balances through Sui gRPC;
- exact-input and exact-output quotes restricted to the configured Cetus pool;
- executable Level1 bid and ask probes;
- real-time swaps from concrete Cetus `SwapEvent` records in the Sui
  checkpoint stream;
- immediate exact-input sells and exact-output buys with slippage protection;
- server-side transaction simulation and gas selection;
- local Ed25519 transaction signing and final execution status;
- current connector-session order and execution status.

`Pools` contains semicolon-separated
`pool-id|base-coin-type|quote-coin-type|security-code` entries. The security
code is optional. The default entry is the canonical Cetus SUI/USDC pool:

```text
0x51e883ba7c0b566a26cbc8a94cd33eb0abd418a77cc1e60ad22fd9b1f29cd2ab|0x2::sui::SUI|0xdba34672e30cb065b1f93e3ab55318768fd6fef66c15942c9f7cb846e2f900e7::usdc::USDC|SUI-USDC
```

Public market data does not require a wallet. A Sui address is required for
balances. Trading additionally requires its Ed25519 private key in
`suiprivkey` Bech32 form or as 32 raw hexadecimal bytes. The key remains local.
The connector simulates each protected swap, lets the node select gas, signs
the returned transaction bytes locally, and verifies the concrete on-chain
`SwapEvent` before publishing the execution.

The public Sui gRPC endpoint is intended for development and can be rate
limited. Use a dedicated Full Node gRPC provider for production workloads.

## Market-data boundary

Cetus is a concentrated-liquidity AMM and has no central-limit order book. The
connector therefore does not invent market depth. Level1 values are executable
Router API probes for the configured pool.

Sui Full Node gRPC checkpoint streaming is the supported replacement for the
deprecated JSON-RPC WebSocket subscriptions. The connector publishes live
ticks only from exact, binary-decoded Cetus `SwapEvent` payloads matching a
configured pool. The public node and Router API do not provide a bounded,
indexed Cetus trade archive, so historical ticks and candles are deliberately
not advertised.

## Official documentation

- [Cetus developer documentation](https://cetus-1.gitbook.io/cetus-developer-docs)
- [Cetus Sui SDK](https://github.com/CetusProtocol/cetus-clmm-sui-sdk)
- [Cetus contracts](https://github.com/CetusProtocol/cetus-contracts)
- [Sui gRPC access](https://docs.sui.io/develop/accessing-data/grpc)
- [Sui JSON-RPC migration](https://docs.sui.io/develop/accessing-data/json-rpc-migration)
- [Sui Full Node protocol](https://docs.sui.io/references/fullnode-protocol)
- [Official Sui protobuf definitions](https://github.com/MystenLabs/sui-apis)
