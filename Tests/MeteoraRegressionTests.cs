namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Meteora;
using StockSharp.Meteora.Native;
using StockSharp.Meteora.Native.Model;

[TestClass]
public class MeteoraRegressionTests : BaseTestClass
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RequestedTradeCountStopsReadingTransactionsWithinThePage()
	{
		using var bytes = new MemoryStream();
		using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
		{
			writer.Write(MeteoraExtensions.SwapDiscriminator);
			writer.Write(new byte[64]);
			writer.Write(0);
			writer.Write(0);
			writer.Write(1_000_000UL);
			writer.Write(2_000_000UL);
			writer.Write(true);
		}
		var log = "Program data: " + Convert.ToBase64String(bytes.ToArray());
		var reads = 0;
		using var client = new MeteoraRpcClient("https://rpc.example.test", MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var body = JObject.Parse(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			JToken result;
			if (body["method"].Value<string>() == "getSignaturesForAddress")
				result = new JArray(new JObject { ["signature"] = new string('1', 64), ["blockTime"] = 1760000000 },
					new JObject { ["signature"] = new string('1', 63) + "2", ["blockTime"] = 1760000000 },
					new JObject { ["signature"] = new string('1', 63) + "3", ["blockTime"] = 1760000000 });
			else
			{
				reads++;
				result = new JObject { ["slot"] = 123, ["blockTime"] = 1760000000, ["meta"] = new JObject { ["logMessages"] = new JArray(log) } };
			}
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = body["id"], ["result"] = result }.ToString()),
			};
		})) { BaseAddress = new("https://rpc.example.test") }, "_httpClient");
		using var adapter = new MeteoraMessageAdapter(new IncrementalIdGenerator());
		typeof(MeteoraMessageAdapter).GetField("_rpcClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter, client);
		var market = new MeteoraMarket
		{
			PoolAddress = new string('1', 32),
			TokenX = new() { Decimals = 6 },
			TokenY = new() { Decimals = 6 },
		};
		var loading = (ValueTask<MeteoraTrade[]>)typeof(MeteoraMessageAdapter).GetMethod("LoadTradesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
			.Invoke(adapter, [market, DateTime.UnixEpoch, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, CancellationToken]);
		var trades = await loading;

		AreEqual(1, trades.Length);
		AreEqual(2m, trades[0].Price);
		AreEqual(1, reads);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TransactionRequestAcceptsVersionOne()
	{
		Task<string> body = null;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			body = request.Content.ReadAsStringAsync(CancellationToken);
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""{"jsonrpc":"2.0","id":1,"result":{"slot":123,"version":1,"blockTime":1760000000,"meta":{"err":null},"transaction":{"message":{"transactionConfig":{}}}}}"""),
			};
		}))
		{
			BaseAddress = new("https://rpc.example.test"),
		};
		using var client = new MeteoraRpcClient(http.BaseAddress.ToString(),
			MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}
}
