# VALR Connector

The connector integrates VALR spot, margin, and perpetual-futures markets
through the current REST and WebSocket APIs.

Supported features:

- discovery of active spot and perpetual pairs with native price, quantity,
  notional, and margin constraints;
- Level1 market summaries, aggregated order books, and public trades through
  REST snapshots and the official trade WebSocket;
- historical OHLCV candles for every interval documented by VALR and realtime
  one-minute trade buckets;
- HMAC-SHA512 authentication, including optional primary-account
  impersonation of a subaccount;
- balances, open perpetual positions, realised and unrealised PnL, and
  leverage;
- market, limit, post-only, stop-loss-limit, and take-profit-limit orders;
- GTC, IOC, and FOK time-in-force, spot margin, and futures reduce-only flags;
- native order modification, individual and filtered bulk cancellation;
- open and historical orders, account fills, and realtime balance, order,
  execution, and position updates through the account WebSocket.

Public REST market data works without credentials. VALR requires an API key
and secret when establishing both documented WebSocket connections. Without
credentials, use history-only subscriptions; with credentials, realtime market
and account streams are enabled automatically. Set `SubAccountId` when a
primary-account key should act on a margin- or futures-enabled subaccount. A
key created directly on that subaccount does not need this setting.

Official documentation:

- [VALR API documentation](https://docs.valr.com/)
- [VALR official API agent reference](https://github.com/valrdotcom/valr-agent-skills)
