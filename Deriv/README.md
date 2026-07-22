# StockSharp Deriv Connector

The connector integrates StockSharp with the current Deriv Options REST and
WebSocket APIs.

Supported features:

- active symbols, Level 1 quotes, tick history, and tick streaming;
- historical and streaming time-frame candles;
- public market-data sessions without credentials;
- authenticated demo and real accounts through the REST OTP workflow;
- proposals, contract purchases, early sell or cancellation, and live contract updates;
- balances, portfolios, open contracts, and transaction streaming;
- automatic subscription restoration with a fresh one-time WebSocket URL.

Configure a Deriv personal access or OAuth token and application ID for private
operations. The account ID is optional when exactly one active account matches
the selected demo or real mode.

Official API documentation: https://developers.deriv.com/docs/
