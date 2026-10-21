namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Curve;
using StockSharp.Curve.Native;
using StockSharp.Curve.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Curve publishes market data without credentials.
/// </summary>
[TestClass]
public class CurveTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(6000, CooperativeCancellation = true)]
	public async Task SocketStartupIsBoundedWhenThePeerDoesNotRespond(bool acceptUpgrade)
	{
		using var server = new WebSocketTestServer();
		using var client = new CurveSocketClient(server.Endpoint, TimeSpan.FromMilliseconds(500));
		var starting = client.ConnectAsync([new CurveMarket
		{
			PoolId = "0x1111111111111111111111111111111111111111",
		}], CancellationToken).AsTask();
		using var socket = acceptUpgrade ? await server.AcceptAsync(CancellationToken) : null;
		if (socket is not null)
		{
			var request = await WebSocketTestServer.ReadRequestAsync(socket, CancellationToken);
			AreEqual("eth_subscribe", request.Value<string>("method"));
		}

		var error = await ThrowsAsync<TimeoutException>(() => starting.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken));

		IsTrue(error.Message.Contains("Curve WebSocket"), error.Message);
		IsFalse(client.IsConnected);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
	{
		var adapter = new CurveMessageAdapter(new IncrementalIdGenerator())
		{
			Pools = "0xbEbc44782C7dB0a1A60Cb6fe97d0b483032FF1C7|0x6B175474E89094C44Da98b954EedeAC495271d0F|0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48|DAI-USDC",
			MaximumDiscoveredPools = 0,
		};
		if (TestContext?.TestName == nameof(Candles))
			// Historical candles use the Curve Prices HTTP API and do not need a live socket.
			adapter.WebSocketEndpoint = null;
		return adapter;
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "DAI-USDC",
		BoardCode = BoardCodes.Curve,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Candles are folded from on chain swaps, and this stablecoin pool can sit idle for hours,
	/// so the history has to reach far enough back to contain any at all.
	/// </remarks>
	protected override int CandlesDepth => 1440;
}
