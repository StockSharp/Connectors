namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinMarketCap;
using StockSharp.Messages;

/// <summary>
/// CoinMarketCap publishes market data without credentials.
/// </summary>
[TestClass]
public class CoinMarketCapMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
	{
		var token = Env("COINMARKETCAP_TOKEN");
		return new CoinMarketCapMessageAdapter(new IncrementalIdGenerator())
		{
			AccessMode = token.IsEmpty() ? CoinMarketCapAccessModes.Keyless : CoinMarketCapAccessModes.ApiKey,
			Token = token.IsEmpty() ? null : token.Secure(),
		};
	}

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2.IsTFCandles &&
			(!Env("COINMARKETCAP_PAID_TESTS").EqualsIgnoreCase("1") || Env("COINMARKETCAP_TOKEN").IsEmpty()))
			Inconclusive("CoinMarketCap OHLCV requires Startup or above. Set COINMARKETCAP_TOKEN and COINMARKETCAP_PAID_TESTS=1 when that plan is available.");
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD",
		BoardCode = BoardCodes.CoinMarketCap,
	};
}
