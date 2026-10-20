namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.ManifestTrade.Native;

[TestClass]
public class ManifestTradeStreamingRegressionTests : BaseTestClass
{
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
}
