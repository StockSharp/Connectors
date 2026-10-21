namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Orca;
using StockSharp.Orca.Native;
using StockSharp.Orca.Native.Model;

/// <summary>
/// Tests for the Orca connector.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OrcaTests : LiveMarketDataTestBase
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
		using var client = new OrcaRpcClient(http.BaseAddress.ToString(),
			OrcaClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}

	private const string _pool = "Czfq3xZZDmsdGdUyrNLtRhGc47cXcZtLG4crryfu44zE";

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ASubscriptionAcknowledgementCompletesTheConnection()
	{
		using var server = new WebSocketTestServer();
		using var client = new OrcaSocketClient(server.Endpoint, (_, _) => default, _ => default);
		var incoming = server.AcceptAsync(CancellationToken);
		var connecting = client.ConnectAsync([_pool], CancellationToken).AsTask();
		using var peer = await incoming;
		var request = await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);
		AreEqual("2.0", request.Value<string>("jsonrpc"));
		AreEqual("logsSubscribe", request.Value<string>("method"));
		AreEqual(_pool, request["params"][0]["mentions"][0].Value<string>());
		AreEqual("confirmed", request["params"][1]["commitment"].Value<string>());
		await WebSocketTestServer.ReplyAsync(peer, request.Value<long>("id"), 42, CancellationToken);

		await connecting.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken);
		IsTrue(client.IsConnected);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task AMissingAcknowledgementTimesOutAndClosesTheSocket()
	{
		using var server = new WebSocketTestServer();
		using var client = new OrcaSocketClient(server.Endpoint, (_, _) => default, _ => default,
			TimeSpan.FromMilliseconds(200));
		var incoming = server.AcceptAsync(CancellationToken);
		var connecting = client.ConnectAsync([_pool], CancellationToken).AsTask();
		using var peer = await incoming;
		await WebSocketTestServer.ReadRequestAsync(peer, CancellationToken);

		await ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken));
		IsTrue(connecting.IsCompleted, "The client did not finish its subscription within its configured timeout.");
		IsFalse(client.IsConnected, "The failed connection left an open socket.");
	}

	/// <inheritdoc />
	protected override TimeSpan ConnectTimeout => TimeSpan.FromSeconds(45);

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OrcaMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com throttles getTransaction after about a dozen
		// calls, which is far less than one page of trade history needs
		RpcEndpoint = "https://solana-rpc.publicnode.com",
		StreamingEndpoint = "wss://solana-rpc.publicnode.com",

		Pools = "Czfq3xZZDmsdGdUyrNLtRhGc47cXcZtLG4crryfu44zE|SOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USDC",
		BoardCode = BoardCodes.Orca,
	};
}
