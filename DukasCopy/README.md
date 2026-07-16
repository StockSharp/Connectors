# StockSharp Dukascopy connector

The connector uses Dukascopy's official JForex SDK. It does not implement or reverse-engineer the private Dukascopy wire protocol. A small local Java bridge hosts the SDK and exposes a typed, loopback-only NDJSON contract to the .NET adapter.

Official documentation:

- [JForex API overview](https://www.dukascopy.com/swiss/english/forex/api/jforex-api/)
- [Using the JForex SDK client](https://www.dukascopy.com/wiki/en/development/get-started-api/use-jforex-sdk/sdk-client/)
- [JForex API reference](https://www.dukascopy.com/client/javadoc/overview-summary.html)
- [Dukascopy Maven repository](https://www.dukascopy.com/wiki/en/development/get-started-api/development-environment/maven-repository/)

## Bridge setup

Build the bridge with Java 8 or newer and Maven:

```shell
mvn -f Bridge/pom.xml clean package
```

The build pins the official `DDS2-jClient-JForex` artifact at `3.6.51` and creates:

```text
Bridge/target/dukascopy-jforex-bridge-1.0.0-all.jar
```

There are two supported launch modes:

1. Set `BridgeJarPath` to the shaded JAR. The connector starts `java -jar` itself and terminates that child process on disconnect.
2. Leave `BridgeJarPath` empty and start the bridge separately:

```shell
java -jar Bridge/target/dukascopy-jforex-bridge-1.0.0-all.jar --port 27431
```

The bridge binds only to the operating system loopback address. Set the same `BridgePort` in StockSharp when using a non-default port.

## Credentials and environments

Set the JForex `UserName`, `Password`, and `IsDemo` properties. Credentials are sent only over the loopback connection and are not logged or persisted by the bridge. The bridge connects directly to the official JForex demo or live trading service; the desktop terminal is not required.

## Supported operations

- instrument lookup from the account's available JForex instruments;
- live quote ticks, Level1, and full depth supplied by `ITick`;
- historical quote ticks;
- historical and live time-frame candles;
- order registration, modification, cancellation, and live order updates;
- account balance/equity/margin updates and open-position snapshots.

StockSharp order volumes are passed to JForex in its native unit: millions. For example, `0.001` means one thousand units for an FX instrument. `DukasCopyOrderCondition` exposes native trigger variants, slippage in pips, stop-loss, take-profit, and an order comment.

## Normalization notes and limitations

- JForex ticks are quote updates, not exchange prints. A StockSharp `Ticks` subscription is therefore normalized to a midpoint price with the combined top-of-book volume. Use `Level1` or `MarketDepth` when bid/ask semantics must be retained.
- Candle prices and volumes are midpoint/combined values built from the JForex bid and ask bars.
- `IEngine.getOrders()` returns current pending orders and open positions. Closed-order history is not claimed by this connector; live close events are still forwarded while connected.
- The Java bridge contains no strategy or signal logic. Its `IStrategy` implementation only obtains the documented SDK context, forwards callbacks, and executes connector commands on the JForex strategy thread.
- Trading availability, instrument coverage, minimum amounts, and market-depth levels depend on the Dukascopy account and environment.
