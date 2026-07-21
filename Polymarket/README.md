# Polymarket CLOB connector for StockSharp

The connector integrates Polymarket outcome-token markets through the current
CLOB REST API, market and user WebSocket channels, Data API, and locally signed
version 2 or version 3 orders on Polygon.

Supported functionality:

- discovery of active prediction markets and each outcome token as a separate
  StockSharp binary-option security;
- realtime Level1, full order-book snapshots and incremental depth updates;
- realtime public trades from the CLOB market WebSocket;
- historical time-frame candles built from the official outcome price-history
  endpoint;
- pUSD collateral balance and outcome-token positions;
- active orders, authenticated trade history, and realtime user order/trade
  events;
- limit, post-only, GTD, FOK, FAK, and marketable order registration;
- individual, per-market, filtered, and account-wide cancellation;
- EOA, Polymarket proxy, Gnosis Safe, and POLY_1271 deposit-wallet signatures.

Public market data requires no credentials. Account access requires a CLOB API
key, URL-safe Base64 API secret, passphrase, and the EOA signer address associated
with those credentials. Trading additionally requires the matching EVM private
key. The key is used locally for EIP-712 or ERC-7739/POLY_1271 order signing and
is never transmitted. Proxy, Safe, and deposit-wallet accounts must also set the
funder address that actually holds collateral and conditional tokens.

StockSharp order volume is always expressed in outcome shares. A market order is
therefore submitted as an immediately executable signed limit order at the worst
price needed to fill the requested shares in the current book. `MatchOrCancel`
maps to FOK; other market orders map to FAK. Polymarket has no atomic replace
operation, so replacement cancels the old order before submitting the new one.
Required pUSD/token approvals and wallet provisioning must already exist on the
Polymarket account.

The price-history endpoint contains price observations but no volume or trade
count. Historical candles consequently publish correct OHLC prices with zero
volume and zero ticks. The public CLOB API does not expose a historical trade
tape, so tick subscriptions are realtime only. Position data comes from the Data
API and may lag on-chain settlement briefly.

All REST and WebSocket payloads use concrete DTOs. The implementation does not
use dynamic JSON trees, anonymous protocol bodies, or protocol dictionaries.

Official resources:

- [Polymarket developer documentation](https://docs.polymarket.com/)
- [Trading overview](https://docs.polymarket.com/trading/overview)
- [CLOB quickstart](https://docs.polymarket.com/trading/quickstart)
- [Market data and market discovery](https://docs.polymarket.com/market-data/fetching-markets)
- [WebSocket overview](https://docs.polymarket.com/market-data/websocket/overview)
- [Market channel](https://docs.polymarket.com/market-data/websocket/market-channel)
- [User channel](https://docs.polymarket.com/market-data/websocket/user-channel)
- [Create orders](https://docs.polymarket.com/trading/orders/create)
- [Cancel orders](https://docs.polymarket.com/trading/orders/cancel)
- [Current positions](https://docs.polymarket.com/api-reference/core/get-current-positions-for-a-user)
- [StockSharp Polymarket connector](https://doc.stocksharp.com/en/topics/api/connectors/crypto_exchanges/polymarket.html)

Polymarket and its mark are trademarks of their respective owner. StockSharp is
not affiliated with or endorsed by Polymarket.
