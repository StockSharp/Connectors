# StockSharp Sierra Chart DTC connector

StockSharp adapter for the open Data and Trading Communications (DTC) Protocol and
the DTC server built into Sierra Chart. The connector uses protocol version 8 with
the official binary encoding with variable-length strings over TCP. All protocol
messages are represented by typed models.

## Connection setup

Enable the DTC server in **Global Settings > Sierra Chart Server Settings**. The
default trading and real-time endpoint is `127.0.0.1:11099`; the separate historical
data endpoint is `127.0.0.1:11098`. Enable **Allow Trading** in Sierra Chart when order
entry is required. Login and password are normally blank for loopback connections,
but must match the Sierra Chart account when server authentication is enabled.

The connector can also connect to other conforming DTC servers. Configure TLS for any
remote trading connection: the DTC specification requires a secure transport when
credentials or trading traffic cross a public network.

## Supported operations

- security search and security definitions;
- Level1, trades, and market depth through native streaming subscriptions;
- tick and time-frame candle history through a separate DTC history connection;
- trade-account discovery, balances, positions, open orders, and historical fills;
- market, limit, stop, and stop-limit orders;
- order cancellation and native cancel/replace;
- DTC heartbeat monitoring and clean reconnect/resubscription through StockSharp.

The historical connection is opened per request because Sierra Chart's historical DTC
port accepts one request per connection. Compression is deliberately disabled, so each
record remains independently framed and validated by the protocol codec.

## Sierra Chart market-data restrictions

Sierra Chart's local DTC server is not a way to redistribute exchange data. Its current
rules reject real-time and historical data for CME Group, Eurex, Nasdaq, Cboe, and US
consolidated equities (UTP/CTA), and generally restrict data access to the local machine.
These restrictions come from exchange and provider agreements and may change. The
connector does not attempt to bypass them. Trading messages remain supported when the
server and account permit trading; other conforming DTC servers may expose a different
set of capabilities.

## Official documentation

- [DTC Protocol overview and current protocol files](https://www.sierrachart.com/index.php?page=doc/DTCProtocol.php)
- [DTC messages and procedures](https://www.sierrachart.com/index.php?page=doc/DTCMessageDocumentation.php)
- [Sierra Chart DTC server setup, ports, and restrictions](https://www.sierrachart.com/index.php?page=doc/DTCServer.php)
- [Official DTC protocol repository](https://github.com/DTC-protocol/DTC/)
