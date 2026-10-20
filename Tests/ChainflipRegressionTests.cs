namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Chainflip.Native;
using StockSharp.Chainflip.Native.Model;

[TestClass]
public class ChainflipRegressionTests : BaseTestClass
{
	private const string _rawPoolPrice = """{"sell":"0x10020a0f99e5bd2a84731bd51","buy":"0x1000000000000000000000000"}""";
	private const string _executableBook = """{"bids":[{"amount":"0x39a849f25","sqrt_price":"0xffd3f4b1ae93dab91b1663d3"}],"asks":[{"amount":"0x8591502e","sqrt_price":"0x100319d4a7fad6ea3dbd4fc96"}]}""";

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task Level1UsesExecutableOrderBookPricesAtTheRequestedBlock()
	{
		// USDT/USDC at mainnet block 15331072 has crossed indicative pool prices.
		const string blockHash = "0x1111111111111111111111111111111111111111111111111111111111111111";
		JObject request = null;
		using var client = CreateClient(_executableBook, value => request = value);
		var prices = await client.GetPricesAsync(Market(), blockHash, CancellationToken);

		AreEqual(ChainflipExtensions.DecodeSqrtPrice("0xffd3f4b1ae93dab91b1663d3", 6, 6), prices.Bid);
		AreEqual(ChainflipExtensions.DecodeSqrtPrice("0x100319d4a7fad6ea3dbd4fc96", 6, 6), prices.Ask);
		IsTrue(prices.Bid < prices.Ask);
		AreEqual("cf_pool_orderbook", request["method"].Value<string>());
		AreEqual(blockHash, request["params"]["at"].Value<string>());
		AreEqual(1, request["params"]["orders"].Value<int>());
	}

	[TestMethod]
	[DataRow("""{"bids":[],"asks":[]}""")]
	[DataRow("""{"bids":[{"amount":"0x0","sqrt_price":"0x0"}],"asks":[]}""")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task EmptyExecutableBooksStillFail(string book)
	{
		using var client = CreateClient(book, _ => { });
		await ThrowsAsync<InvalidDataException>(async () => await client.GetPricesAsync(Market(), null, CancellationToken));
	}

	private static ChainflipMarket Market() => new()
	{
		BaseAsset = new() { Chain = "Ethereum", Symbol = "USDT", Decimals = 6 },
		QuoteAsset = new() { Chain = "Ethereum", Symbol = "USDC", Decimals = 6 },
		SecurityCode = "USDT@ETHEREUM-USDC@ETHEREUM",
	};

	private ChainflipStateClient CreateClient(string book, Action<JObject> capture)
	{
		var client = new ChainflipStateClient("https://rpc.example.test");
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var payload = JObject.Parse(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			capture(payload);
			var result = payload["method"].Value<string>() == "cf_pool_orderbook" ? book : _rawPoolPrice;
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{result}}}"),
			};
		})), "_http");
		return client;
	}
}
