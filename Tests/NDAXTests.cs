namespace StockSharp.Connectors.Tests;

using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.NDAX;
using StockSharp.NDAX.Native;

/// <summary>
/// NDAX publishes market data without credentials.
/// </summary>
[TestClass]
public class NDAXTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow("{\"msg\":\"PONG\"}", true)]
	[DataRow("{\"msg\":\"unexpected\"}", false)]
	[DataRow("{\"result\":true}", false)]
	[DataRow("{\"msg\":null}", false)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SocketHeartbeatValidatesThePongObject(string payload, bool isValid)
	{
		using var server = new WebSocketTestServer();
		using var client = new NDAXSocketClient("wss://socket.example.test", new WorkingTime(), 0, 1, null, null, 0);
		// Keep production endpoint validation intact while exercising the wire protocol locally.
		typeof(NDAXSocketClient).GetField("_endpoint", BindingFlags.Instance | BindingFlags.NonPublic)
			.SetValue(client, server.Endpoint);
		var connecting = client.ConnectAsync(CancellationToken);
		using var socket = await server.AcceptAsync(CancellationToken);
		await connecting;

		var ping = client.PingAsync(CancellationToken).AsTask();
		var request = await WebSocketTestServer.ReadRequestAsync(socket, CancellationToken);
		AreEqual("Ping", request.Value<string>("n"));
		var reply = new JObject
		{
			["m"] = 1,
			["i"] = request["i"],
			["n"] = "Ping",
			["o"] = payload,
		};
		await socket.SendAsync(Encoding.UTF8.GetBytes(reply.ToString()), WebSocketMessageType.Text, true, CancellationToken);

		if (isValid)
			await ping;
		else
			await ThrowsAsync<InvalidDataException>(() => ping);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new NDAXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCCAD",
		BoardCode = BoardCodes.NDAX,
	};

	// ticker, order book and candles arrive at once, but the NDAX tape is far too thin for a
	// bounded wait: BTCCAD is the busiest instrument of the venue and still prints only about
	// one trade every three minutes, and the socket trade subscription carries no history
	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes => [DataType.Ticks];
}
