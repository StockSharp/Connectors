namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Orca.Native;

[TestClass]
public class OrcaStreamingRegressionTests : BaseTestClass
{
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
}
