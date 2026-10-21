namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Lfj;
using StockSharp.Lfj.Native;
using StockSharp.Messages;

/// <summary>
/// Tests for the Lfj connector.
/// </summary>
[TestClass]
public class LfjTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RpcReadHonorsRetryAfter()
	{
		var timer = Stopwatch.StartNew();
		var requests = new List<TimeSpan>();
		using var client = new LfjRpcClient("https://rpc.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(_ =>
		{
			requests.Add(timer.Elapsed);
			if (requests.Count == 1)
			{
				var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
				{
					Content = new StringContent("rate limited"),
				};
				response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
				return response;
			}
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":"0x64"}"""),
			};
		})), "_http");

		var block = await client.GetLatestBlockNumberAsync(CancellationToken);

		AreEqual(new BigInteger(100), block);
		AreEqual(2, requests.Count);
		IsTrue(requests[1] - requests[0] >= TimeSpan.FromMilliseconds(950),
			$"RPC retried before Retry-After elapsed: {requests[1] - requests[0]}.");
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
	{
		var adapter = new LfjMessageAdapter(new IncrementalIdGenerator());
		if (TestContext?.TestName is nameof(Ticks) or nameof(Candles))
			// These requests read historical swaps over HTTP RPC and do not use the live socket.
			adapter.WebSocketEndpoint = null;
		return adapter;
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WAVAX-USDC-LB10",
		BoardCode = BoardCodes.Lfj,
	};

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
		{
			// This on-chain minute contains swaps; recent minutes can have no trades in this pool.
			message.From = new DateTime(2026, 10, 19, 22, 15, 0, DateTimeKind.Utc);
			message.To = message.From.Value.AddMinutes(1);
			message.Count = 1;
		}
	}
}
