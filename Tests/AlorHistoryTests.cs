namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AlorHistory;
using StockSharp.Messages;

/// <summary>
/// Tests for the AlorHistory connector.
/// </summary>
[TestClass]
public class AlorHistoryTests : LiveMarketDataTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlorHistoryLookupSber()
	{
		SkipUnlessLive();

		var adapter = CreateAdapter<AlorHistoryMessageAdapter>();
		var counter = 0;
		await foreach (var sec in DownloadSecurities(adapter, new() { SecurityId = new() { SecurityCode = "SBER" }, Count = 50 }).WithCancellation(CancellationToken))
		{
			IsNotNull(sec.SecurityId.SecurityCode);
			counter++;
		}

		IsGreater(counter, 0, "No securities found for SBER");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlorHistoryLookupAll()
	{
		SkipUnlessLive();

		var adapter = CreateAdapter<AlorHistoryMessageAdapter>();
		var counter = 0;
		await foreach (var sec in DownloadSecurities(adapter, new() { Count = 50 }).WithCancellation(CancellationToken))
		{
			IsNotNull(sec.SecurityId.SecurityCode);
			counter++;
			if (counter >= 50)
				break;
		}

		IsGreater(counter, 0, "No securities found");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlorHistoryCandlesSber1d()
	{
		SkipUnlessLive();

		var adapter = CreateAdapter<AlorHistoryMessageAdapter>();
		var secId = "SBER@TQBR".ToSecurityId();
		var from = new DateTime(2024, 1, 10);
		var to = new DateTime(2024, 1, 20);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(adapter, secId, TimeSpan.FromDays(1), from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from.AddDays(-1), to.AddDays(1), ref lastTime);
			counter++;
		}

		IsGreater(counter, 0, "No candles found for SBER daily");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlorHistoryCandlesGazp1h()
	{
		SkipUnlessLive();

		var adapter = CreateAdapter<AlorHistoryMessageAdapter>();
		var secId = "GAZP@TQBR".ToSecurityId();
		var from = new DateTime(2024, 6, 3);
		var to = new DateTime(2024, 6, 4);

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(adapter, secId, TimeSpan.FromHours(1), from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from.AddDays(-1), to.AddDays(1), ref lastTime);
			counter++;
		}

		IsGreater(counter, 0, "No candles found for GAZP hourly");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task AlorHistoryCandlesSber1m()
	{
		SkipUnlessLive();

		var adapter = CreateAdapter<AlorHistoryMessageAdapter>();
		var secId = "SBER@TQBR".ToSecurityId();
		var from = new DateTime(2024, 6, 3, 10, 0, 0).UtcKind();
		var to = new DateTime(2024, 6, 3, 11, 0, 0).UtcKind();

		var lastTime = ResetTimeOrder();
		var counter = 0;
		await foreach (var msg in DownloadCandles(adapter, secId, TimeSpan.FromMinutes(1), from, to).WithCancellation(CancellationToken))
		{
			Validate(msg, secId, from.AddHours(-1), to.AddHours(1), ref lastTime);
			counter++;
		}

		IsGreater(counter, 0, "No candles found for SBER 1-min");
	}

	[TestMethod]
	public async Task AlorHistorySettingsSaveLoad()
	{
		var adapter = new AlorHistoryMessageAdapter(new IncrementalIdGenerator())
		{
			Exchanges = "MOEX"
		};

		var storage = new SettingsStorage();
		await adapter.SaveAsync(storage, CancellationToken);

		var adapter2 = new AlorHistoryMessageAdapter(new IncrementalIdGenerator());
		await adapter2.LoadAsync(storage, CancellationToken);

		AreEqual("MOEX", adapter2.Exchanges);
	}

	[TestMethod]
	public void AlorHistoryTimeFrames()
	{
		var tfs = AlorHistoryMessageAdapter.AllTimeFrames.ToList();

		IsGreater(tfs.Count, 0, "No timeframes");
		IsTrue(tfs.Contains(TimeSpan.FromDays(1)), "Missing daily TF");
		IsTrue(tfs.Contains(TimeSpan.FromHours(1)), "Missing hourly TF");
		IsTrue(tfs.Contains(TimeSpan.FromMinutes(1)), "Missing 1-min TF");
	}

	[TestMethod]
	public void AlorHistoryIsAllDownloading()
	{
		var adapter = new AlorHistoryMessageAdapter(new IncrementalIdGenerator());

		IsTrue(adapter.IsAllDownloadingSupported(DataType.Securities));
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new AlorHistoryMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SBER",
		BoardCode = BoardCodes.Moex,
	};
}
