namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coinhako;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Coinhako.</summary>
[TestClass]
[RegionalLiveTests]
public class CoinhakoTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinhakoMessageAdapter(new IncrementalIdGenerator())
		{
			CounterCurrencies = "SGD",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-SGD",
		BoardCode = BoardCodes.Coinhako,
	};
}
