namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.ManifestTrade;
using StockSharp.ManifestTrade.Native;
using StockSharp.ManifestTrade.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the ManifestTrade connector.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ManifestTradeTests : LiveMarketDataTestBase
{
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
		using var client = new ManifestTradeRpcClient(http.BaseAddress.ToString(),
			ManifestTradeClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}

	private const string _market = "8sjV1AqBFvFuADBCQHhotaRq5DFFYSjjg1jMyVWMqXvZ";

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task BothSubscriptionsMustBeAcknowledged()
	{
		using var server = new WebSocketTestServer();
		using var client = new ManifestTradeSocketClient(server.Endpoint, (_, _, _) => default, (_, _) => default, _ => default);
		var incoming = server.AcceptAsync(CancellationToken);
		var connecting = client.ConnectAsync([_market], CancellationToken).AsTask();
		using var peer = await incoming;
		var account = await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
		var logs = await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
		AreEqual("accountSubscribe", account.Value<string>("method"));
		AreEqual(_market, account["params"][0].Value<string>());
		AreEqual("logsSubscribe", logs.Value<string>("method"));
		AreEqual(_market, logs["params"][0]["mentions"][0].Value<string>());
		await WebSocketTestServer.ReplyAsync(peer, account.Value<long>("id"), 42, CancellationToken);
		IsFalse(connecting.IsCompleted, "An account acknowledgement cannot confirm the log subscription.");
		await WebSocketTestServer.ReplyAsync(peer, logs.Value<long>("id"), 43, CancellationToken);

		await connecting.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken);
		IsTrue(client.IsConnected);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task AMissingLogAcknowledgementTimesOutAndClosesTheSocket()
	{
		using var server = new WebSocketTestServer();
		using var client = new ManifestTradeSocketClient(server.Endpoint, (_, _, _) => default, (_, _) => default, _ => default,
			TimeSpan.FromMilliseconds(500));
		var incoming = server.AcceptAsync(CancellationToken);
		var connecting = client.ConnectAsync([_market], CancellationToken).AsTask();
		using var peer = await incoming;
		var account = await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
		await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
		await WebSocketTestServer.ReplyAsync(peer, account.Value<long>("id"), 42, CancellationToken);

		await ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken));
		IsTrue(connecting.IsCompleted, "The unconfirmed log subscription held the connection open.");
		IsFalse(client.IsConnected, "The failed connection left an open socket.");
	}

	/// <inheritdoc />
	protected override TimeSpan ConnectTimeout => TimeSpan.FromSeconds(45);

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ManifestTradeMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com throttles getTransaction after about a dozen
		// calls, which is far less than one page of trade history needs
		RpcEndpoint = "https://solana-rpc.publicnode.com",
		StreamingEndpoint = "wss://solana-rpc.publicnode.com",

		// the SOL-USDC book only sees quote churn, so a history page of the newest
		// transactions holds no fill at all - USDT-USDC is the busiest Manifest market
		Markets = "8sjV1AqBFvFuADBCQHhotaRq5DFFYSjjg1jMyVWMqXvZ|USDT|USDC",
		MaximumDiscoveredMarkets = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT-USDC",
		BoardCode = BoardCodes.ManifestTrade,
	};
}
