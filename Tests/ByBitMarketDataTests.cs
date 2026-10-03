namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.ByBit;
using StockSharp.Messages;

/// <summary>
/// ByBit publishes market data without credentials.
/// </summary>
[TestClass]
public class ByBitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ByBitMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [ByBitSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.ByBit,
	};

	/// <summary>
	/// A spot instrument says how many decimals its prices have.
	/// </summary>
	/// <remarks>
	/// The venue gives a price scale for its derivatives alone, so its spot instruments came without one, and a
	/// product that checks for it - Hydra marks such an instrument as not set up - took them for broken.
	/// </remarks>
	[TestMethod]
	[Timeout(120000)]
	public Task ASpotInstrumentSaysHowManyDecimalsItsPricesHave() => RunAsync(async harness =>
	{
		var result = await harness.LookupSecuritiesAsync(SecuritiesLimit, SecuritiesTimeout, CancellationToken);

		var instrument = result.Securities.Single(security => security.SecurityId == TestSecurityId);

		IsNotNull(instrument.PriceStep, "The instrument has no price step.");
		AreEqual(instrument.PriceStep.Value.GetCachedDecimals(), instrument.Decimals);
	});

	/// <summary>
	/// A session let go after a disconnect ends without errors.
	/// </summary>
	/// <remarks>
	/// The reset that follows a disconnect asked every socket, closed by then, to close again, and each one answered
	/// with an error: a product that looked instruments up and let the connection go showed one per section.
	/// </remarks>
	[TestMethod]
	[Timeout(120000)]
	public Task ASessionLetGoAfterADisconnectEndsWithoutErrors() => RunAsync(async harness =>
	{
		await harness.DisconnectAsync(ConnectTimeout, CancellationToken);

		var reader = harness.CreateReader();
		await harness.Adapter.SendInMessageAsync(new ResetMessage(), CancellationToken);
		await reader.WaitAsync<ResetMessage>(null, ConnectTimeout, CancellationToken);

		AreEqual(0, harness.Errors.Length, string.Join(Environment.NewLine, harness.Errors.Select(error => error.Message)));
	});

	/// <summary>
	/// A day of minute candles from before the latest thousand of them.
	/// </summary>
	/// <remarks>
	/// The venue starts a page of candles at the moment it is given only when the moment is sent under the name it
	/// knows. Sent under another, it is ignored and the latest thousand come back - every one of them after a day that
	/// is days old, so the day came back empty.
	/// </remarks>
	[TestMethod]
	[Timeout(120000)]
	public Task ADayOfCandlesFromDaysAgo() => RunAsync(async harness =>
	{
		var from = DateTime.UtcNow.Date.AddDays(-3);
		var to = from.AddDays(1);

		var result = await harness.SubscribeAsync<TimeFrameCandleMessage>(
			TimeSpan.FromMinutes(1).TimeFrame(), TestSecurityId,
			mdMsg =>
			{
				mdMsg.From = from;
				mdMsg.To = to;
			},
			null,
			DataTimeout, CancellationToken);

		CheckData(result, "candles");

		AreEqual(from, result.Data.OpenTime, "The first candle is not the first minute of the day asked for.");
	});
}
