namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinW;
using StockSharp.CoinW.Native;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for CoinW.</summary>
[TestClass]
public class CoinWTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SpotDepthIsAnObjectInsideTheResponseEnvelope()
	{
		await using var server = new HistoryHttpServer(() =>
			"{\"code\":200,\"data\":{\"pair\":\"BTC_USDT\",\"bids\":[[\"100\",\"2\"]],\"asks\":[[\"101\",\"3\"]]}}", CancellationToken);
		using var client = new CoinWRestClient(server.Address.ToString(), server.Address.ToString(), null, null);

		var book = await client.GetSpotOrderBookAsync("BTC_USDT", 20, CancellationToken);

		IsNotNull(book);
		AreEqual("100", book.Bids[0].Price);
		AreEqual("2", book.Bids[0].Volume);
		AreEqual("101", book.Asks[0].Price);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinWMessageAdapter(new IncrementalIdGenerator())
		{
			Sections = [CoinWSections.Spot],
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.CoinW,
	};
}
