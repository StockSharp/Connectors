namespace StockSharp.Connectors.Tests;

using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Messages;
using StockSharp.Okex;

/// <summary>
/// Okex publishes market data without credentials.
/// </summary>
[TestClass]
public class OkexMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow(" ")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task InstrumentFamiliesWithoutAnInstrumentIdAreNotSecurities(string placeholderId)
	{
		var response = JsonConvert.SerializeObject(new
		{
			code = "0",
			data = new[]
			{
				new { instId = placeholderId, instType = "FUTURES", instFamily = "IBM-USD_UM_XPERP", state = "preopen" },
				new { instId = "BTC-USDT-261225", instType = "FUTURES", instFamily = "BTC-USDT", state = "live" },
			},
		});
		await using var server = new HistoryHttpServer(() => response, CancellationToken);
		using var auth = new Okex.Native.Authenticator(null, null, null, false);
		using var client = new Okex.Native.HttpClient(server.Address.ToString().TrimEnd('/'), auth);

		var instruments = (await client.GetInstrumentsAsync("FUTURES", false, null, CancellationToken)).ToArray();

		AreEqual(1, instruments.Length);
		AreEqual("BTC-USDT-261225", instruments[0].Id);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OkexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Okex,
	};
}
