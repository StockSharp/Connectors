namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Logging;
using Ecng.Net;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Kucoin;
using StockSharp.Kucoin.Native;
using Futures = StockSharp.Kucoin.Native.Futures;
using Spot = StockSharp.Kucoin.Native.Spot;
using StockSharp.Messages;

/// <summary>
/// Tests for the Kucoin connector.
/// </summary>
[TestClass]
[CLSCompliant(false)]
public class KucoinTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false, false)]
	[DataRow(true, false)]
	[DataRow(false, true)]
	[DataRow(true, true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task HeartbeatPingsEveryExistingSocket(bool futures, bool transactional)
	{
		using var adapter = new KucoinMessageAdapter(new IncrementalIdGenerator());
		using BaseNativeAdapter native = futures
			? new Futures.FuturesAdapter(adapter, (_, _) => default)
			: new Spot.SpotAdapter(adapter, (_, _) => default);
		using var publicServer = new WebSocketTestServer();
		using var privateServer = transactional ? new WebSocketTestServer() : null;
		using var publicClient = CreateClient(publicServer, false);
		using var privateClient = transactional ? CreateClient(privateServer, true) : null;
		const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
		native.GetType().GetField("_publicClient", fields).SetValue(native, publicClient);
		native.GetType().GetField("_privateClient", fields).SetValue(native, privateClient);
		typeof(KucoinMessageAdapter).GetField(futures ? "_futuresAdapter" : "_spotAdapter", fields).SetValue(adapter, native);
		var errors = new List<Exception>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is ErrorMessage error)
				errors.Add(error.Error);
			return default;
		};
		var publicConnection = publicServer.AcceptAsync(CancellationToken);
		await ((IConnection)publicClient).ConnectAsync(CancellationToken);
		using var publicPeer = await publicConnection;
		var privateConnection = privateServer?.AcceptAsync(CancellationToken);
		if (privateClient is not null)
			await ((IConnection)privateClient).ConnectAsync(CancellationToken);
		using var privatePeer = privateConnection is null ? null : await privateConnection;

		for (var i = 0; i < 2; i++)
		{
			await adapter.SendInMessageAsync(new TimeMessage(), CancellationToken);
			AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
			AreEqual("ping", (await WebSocketTestServer.ReadRequestAsync(publicPeer, CancellationToken)).Value<string>("type"));
			if (privatePeer is not null)
				AreEqual("ping", (await WebSocketTestServer.ReadRequestAsync(privatePeer, CancellationToken)).Value<string>("type"));
		}

		BaseLogReceiver CreateClient(WebSocketTestServer server, bool isPrivate) => (futures, isPrivate) switch
		{
			(false, false) => new Spot.PublicSocketClient(server.Endpoint, "test-token", TimeSpan.Zero, 0, new()),
			(false, true) => new Spot.PrivateSocketClient(server.Endpoint, "test-token", TimeSpan.Zero, 0, new()),
			(true, false) => new Futures.PublicSocketClient(server.Endpoint, "test-token", TimeSpan.Zero, 0, new()),
			(true, true) => new Futures.PrivateSocketClient(server.Endpoint, "test-token", TimeSpan.Zero, 0, new()),
		};
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new KucoinMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [KucoinSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.Kucoin,
	};
}
