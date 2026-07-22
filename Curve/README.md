# StockSharp Curve Connector

The connector integrates StockSharp with Curve AMM pools on Ethereum. It uses
the official Curve API for pool discovery, the official Curve Prices API for
historical trades, Ethereum JSON-RPC for contract state and transactions, and
WebSocket `eth_subscribe` log notifications for live swaps.

## Access

Public market data does not require an API key. The default HTTP and WebSocket
URLs are public Ethereum endpoints and may impose request, connection, or log
limits. Production systems should configure dedicated Ethereum RPC endpoints.

A wallet address is required for portfolio balances. Trading additionally
requires the matching private key. The key is used locally for EVM transaction
signing and is never sent to Curve, the Curve APIs, or the RPC provider.

## Supported operations

- high-TVL Ethereum pool discovery through the official pools API;
- explicit pool and pair configuration;
- StableSwap, CryptoSwap, Twocrypto NG, and Tricrypto NG direct pool routes;
- pair securities for every supported coin pair in a discovered pool;
- executable bid and ask Level1 probes through Curve Router NG;
- historical trades from the official Curve Prices feed;
- live `TokenExchange` events through Ethereum WebSocket subscriptions, with
  Curve Prices polling as an overlapping fallback;
- time-frame candles aggregated from actual trades;
- native ETH and discovered ERC-20 wallet balances;
- immediate market sells and buys, ERC-20 approvals, local EIP-1559 or legacy
  signing, receipt tracking, orders, fills, and gas commissions.

`Pools` accepts semicolon-separated items in one of these forms:

- `pool` to expose every direct pair in that pool;
- `pool|base-token|quote-token` to expose one oriented pair;
- `pool|base-token|quote-token|security-code` to also set its StockSharp code.

`MaximumDiscoveredPools` limits automatic discovery, while `MinimumPoolTvl`
filters it by USD TVL. Explicit pools are loaded independently of those limits.
Pool, router, and token contract code is validated on-chain, and token decimals
from the API must match the ERC-20 contract before a market is registered.

## Important boundaries

Curve is an AMM and has no central limit-order book. The connector publishes
executable quote probes as Level1 values and does not invent resting orders or
market depth. Swaps are immediate market operations and cannot be cancelled,
replaced, made post-only, or assigned a time in force.

Direct Router NG routes are used. Native-asset sentinel routes, wrapped/native
conversion, multi-pool routing, liquidity provision, gauges, staking, lending,
and governance are outside the current connector. ERC-20 and wrapped-token
pools are supported. For a StockSharp buy volume, the connector finds the
required quote-token input with Router NG quotes and submits that exact input
with a slippage-adjusted minimum base-token output.

Historical requests use the bounded Curve Prices trade feed and return at most
`HistoryMaximum` trades per query. Live events are decoded only when their coin
indices match the selected pair. Order-status history covers transactions
submitted during the current adapter session because a Curve swap does not
create a persistent exchange order.

## Official documentation

- [Curve developer documentation](https://docs.curve.finance/)
- [Curve pools API documentation](https://api.curve.finance/v1/documentation/)
- [Curve Prices API documentation](https://prices.curve.finance/feeds-docs)
- [Official Curve contracts](https://github.com/curvefi)
- [Official Curve assets](https://github.com/curvefi/curve-assets)
- [Ethereum JSON-RPC specification](https://ethereum.org/en/developers/apis/json-rpc/)
