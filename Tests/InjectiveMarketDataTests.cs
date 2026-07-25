namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Injective;
using StockSharp.Messages;

/// <summary>
/// Injective publishes market data without credentials.
/// </summary>
[TestClass]
public class InjectiveMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new InjectiveMessageAdapter(new IncrementalIdGenerator())
	{
		Environment = InjectiveEnvironments.Mainnet,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "INJ-USDT",
		BoardCode = BoardCodes.Injective,
	};
}
