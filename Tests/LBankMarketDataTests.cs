namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.LBank;
using StockSharp.Messages;

/// <summary>
/// LBank publishes market data without credentials.
/// </summary>
[TestClass]
public class LBankMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task DefaultConnectionsUseThePublishedPublicServices(bool loadEmptySettings)
	{
		using var adapter = new LBankMessageAdapter(new IncrementalIdGenerator());
		if (loadEmptySettings)
			await adapter.LoadAsync(new SettingsStorage(), CancellationToken);

		AreEqual("https://api.lbank.info/v2", adapter.RestEndpoint);
		AreEqual("wss://api.lbank.info/ws/V2/", adapter.WebSocketEndpoint);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExplicitConnectionAddressesSurviveSettingsRoundTrip()
	{
		using var source = new LBankMessageAdapter(new IncrementalIdGenerator())
		{
			RestEndpoint = "https://rest.example.test/v2",
			WebSocketEndpoint = "wss://stream.example.test/ws/V2/",
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);
		using var restored = new LBankMessageAdapter(new IncrementalIdGenerator());
		await restored.LoadAsync(storage, CancellationToken);

		AreEqual(source.RestEndpoint, restored.RestEndpoint);
		AreEqual(source.WebSocketEndpoint, restored.WebSocketEndpoint);
	}

	/// <inheritdoc />
	protected override TimeSpan ConnectTimeout => TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LBankMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.LBank,
	};
}
