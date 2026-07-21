# StockSharp Talos Connector

The connector integrates StockSharp with a customer-provisioned Talos FIX
session. Talos is an institutional OMS/EMS and liquidity aggregation platform;
it is not a public retail exchange with one universal hostname or credential
set.

## Access and configuration

Talos provisions connectivity and technical documentation during client
onboarding. Configure the values from that onboarding package:

- `Address`: the assigned FIX hostname and port;
- `SenderCompId` and `TargetCompId`: the assigned FIX session identifiers;
- `Login` and `Password` when the session profile requires FIX tags 553/554;
- `ClientCode`, `Accounts`, or the StockSharp portfolio name when the profile
  requires account routing;
- `TargetHost` when the TLS certificate name differs from the FIX hostname;
- an optional client certificate and its password when mutual TLS is enabled.

There is deliberately no guessed production endpoint. The initial port is
zero, and the adapter refuses to connect until an endpoint is supplied. TLS
1.2, remote-certificate validation, and certificate-revocation checks are
enabled by default. Disable or alter them only when the Talos onboarding
instructions for a private circuit explicitly require it.

Talos may provision market-data, order-entry, and drop-copy sessions
separately. In that case, create one adapter instance per FIX session and use
the corresponding instance only for the capabilities enabled on that session.

## Protocol coverage

The adapter uses the StockSharp FIX engine with the Talos FIX 4.4 profile. It
provides the standard session flow, including Logon, Heartbeat, Test Request,
Resend Request, Sequence Reset, and Logout. Depending on the entitlements of
the provisioned Talos session, the standard mapping covers:

- security definitions and reference-data responses;
- real-time trades, top of book, and market-depth snapshots or increments;
- new, cancel, replace, and status requests for standard FIX orders;
- execution reports, fills, rejects, and unsolicited/drop-copy executions;
- portfolios, positions, and account-routed orders when enabled by Talos.

Talos-specific algorithms, liquidity-provider routing fields, allocations,
RFQ extensions, and custom tags vary by contracted session profile. They must
be added from the client's current Talos specification rather than inferred.
The adapter does not claim REST or WebSocket support because those schemas and
endpoints are available only in the authenticated Talos knowledge base.

FIX payloads are encoded and decoded as typed protocol messages. The connector
does not use dynamic JSON, JSON trees, anonymous protocol objects, protocol
dictionaries, or untyped object arrays.

## Official resources

- [Talos](https://www.talos.com/)
- [Talos institutional trading platform](https://www.talos.com/our-solutions/trading)
- [Talos analytics and API overview](https://www.talos.com/our-solutions/analytics)
- [Talos authenticated knowledge base](https://kb.talostrading.com/)
