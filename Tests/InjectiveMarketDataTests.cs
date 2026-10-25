namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Injective;
using StockSharp.Messages;

/// <summary>
/// Injective publishes market data without credentials.
/// </summary>
[TestClass]
[RegionalLiveTests]
public class InjectiveMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task AnUnavailableIndexerRetainsTheHttpStatusAfterBoundedRetries()
	{
		await using var server = new HistoryHttpServer(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
		{
			Content = new StringContent("no healthy upstream"),
		}, CancellationToken);
		await using var harness = new MarketDataTestHarness(new InjectiveMessageAdapter(new IncrementalIdGenerator())
		{
			IndexerEndpoint = server.Address.ToString(),
			ChainEndpoint = server.Address.ToString(),
			HeartbeatInterval = TimeSpan.Zero,
		});

		var error = await ThrowsAsync<HttpRequestException>(() =>
			harness.ConnectAsync(TimeSpan.FromSeconds(3), CancellationToken));

		AreEqual(HttpStatusCode.ServiceUnavailable, error.StatusCode);
		IsTrue(error.Message.Contains("no healthy upstream"), error.Message);
		AreEqual(6, server.RequestTimes.Count);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new InjectiveMessageAdapter(new IncrementalIdGenerator())
	{
		Environment = InjectiveEnvironments.Mainnet,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "INJ-USDT",
		BoardCode = BoardCodes.Injective,
	};

	/// <inheritdoc />
	// INJ/USDT spot candles can be hours apart; cover a day of one-minute history.
	protected override int CandlesDepth => 24 * 60;
}
