namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinMarketCap;
using StockSharp.Messages;

/// <summary>
/// CoinMarketCap checks use an API key or explicit access to the anonymous quota.
/// </summary>
[TestClass]
public class CoinMarketCapMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> CreateAdapter(Env("COINMARKETCAP_TOKEN"), Env("COINMARKETCAP_KEYLESS_TESTS"));

	internal static CoinMarketCapMessageAdapter CreateAdapter(string token, string keylessSetting)
	{
		if (token.IsEmpty() && !keylessSetting.EqualsIgnoreCase("1"))
			Inconclusive("Set COINMARKETCAP_TOKEN, or COINMARKETCAP_KEYLESS_TESTS=1 to use the limited anonymous quota.");

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
