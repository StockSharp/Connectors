namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Nado;

/// <summary>
/// Nado publishes market data without credentials.
/// </summary>
[TestClass]
public class NadoMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ConnectionDoesNotFetchQuotesUntilTheInstrumentIsSubscribed(bool isMissing)
	{
		var priceRequests = new List<int[]>();
		HistoryHttpServer http = null;
		await using (http = new HistoryHttpServer(() =>
		{
			var query = http.RequestBody.IsEmpty() ? null : JObject.Parse(http.RequestBody);
			var response = query?.Value<string>("type") switch
			{
				"status" => """{"status":"success","data":"active"}""",
				"contracts" => """{"status":"success","data":{"chain_id":"57073","endpoint_addr":"0x05ec92d78ed421f3d3ada77ffde167106565974e"}}""",
				"all_products" => """{"status":"success","data":{"perp_products":[{"product_id":2,"oracle_price_x18":"100000000000000000000","index_price_x18":"100000000000000000000","book_info":{"size_increment":"1000000000000000","price_increment_x18":"10000000000000000","min_size":"1000000000000000"}},{"product_id":4,"book_info":{"size_increment":"1000000000000000","price_increment_x18":"10000000000000000","min_size":"1000000000000000"}}]}}""",
				"market_prices" => ReadPrices(query),
				null => """[{"product_id":2,"ticker_id":"BTC-PERP_USDT0","base":"BTC-PERP","quote":"USDT0"},{"product_id":4,"ticker_id":"ETH-PERP_USDT0","base":"ETH-PERP","quote":"USDT0"}]""",
				_ => throw new InvalidOperationException($"Unexpected Nado request: {query}."),
			};
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
		}, CancellationToken))
		{
			using var socket = new WebSocketTestServer();
			var adapter = new NadoMessageAdapter(new IncrementalIdGenerator())
			{
				GatewayEndpoint = http.Address.ToString(),
				GatewayV2Endpoint = http.Address.ToString(),
				ArchiveEndpoint = http.Address.ToString(),
				ArchiveV2Endpoint = http.Address.ToString(),
				WebSocketEndpoint = socket.Endpoint,
				HeartbeatInterval = TimeSpan.Zero,
			};
			await using var harness = new MarketDataTestHarness(adapter);
			var accepting = socket.AcceptAsync(CancellationToken);
			await harness.ConnectAsync(TimeSpan.FromSeconds(3), CancellationToken);
			using var peer = await accepting;

			AreEqual(0, priceRequests.Count, "Connecting must not request quotes for unrelated instruments.");
			var result = await harness.SubscribeAsync<Level1ChangeMessage>(DataType.Level1, TestSecurityId,
				null, null, TimeSpan.FromSeconds(3), CancellationToken);

			AreEqual(1, priceRequests.Count);
			AreEqual(1, priceRequests[0].Length);
			AreEqual(2, priceRequests[0][0]);
			if (isMissing)
			{
				IsNotNull(result.Error);
				IsTrue(result.Error.Message.Contains("2015"), result.Error.Message);
				IsNull(result.Data);
			}
			else
			{
				CheckData(result, "level1");
				AreEqual(99m, result.Data.Changes[Level1Fields.BestBidPrice]);
				AreEqual(101m, result.Data.Changes[Level1Fields.BestAskPrice]);
			}
		}

		string ReadPrices(JObject query)
		{
			var productIds = query["product_ids"].ToObject<int[]>();
			priceRequests.Add(productIds);
			return isMissing || Array.Exists(productIds, static id => id != 2)
				? """{"status":"failure","error_code":2015,"error":"The market for the given product was not found."}"""
				: """{"status":"success","data":{"market_prices":[{"product_id":2,"bid_x18":"99000000000000000000","ask_x18":"101000000000000000000"}]}}""";
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new NadoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-PERP_USDT0",
		BoardCode = BoardCodes.Nado,
	};
}
