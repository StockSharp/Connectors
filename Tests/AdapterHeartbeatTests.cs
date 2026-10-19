namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;

[TestClass]
[CLSCompliant(false)]
public class AdapterHeartbeatTests : BaseTestClass
{
	[TestMethod]
	[DataRow(typeof(StockSharp.Cetus.CetusMessageAdapter))]
	[DataRow(typeof(StockSharp.GMTrade.GMTradeMessageAdapter))]
	[DataRow(typeof(StockSharp.Drift.DriftMessageAdapter))]
	[DataRow(typeof(StockSharp.CoinMarketCap.CoinMarketCapMessageAdapter))]
	[DataRow(typeof(StockSharp.FluidDex.FluidDexMessageAdapter))]
	[DataRow(typeof(StockSharp.Balancer.BalancerMessageAdapter))]
	[DataRow(typeof(StockSharp.Jupiter.JupiterMessageAdapter))]
	[DataRow(typeof(StockSharp.Curve.CurveMessageAdapter))]
	[DataRow(typeof(StockSharp.SunIo.SunIoMessageAdapter))]
	[DataRow(typeof(StockSharp.THORChain.THORChainMessageAdapter))]
	[DataRow(typeof(StockSharp.Lfj.LfjMessageAdapter))]
	[DataRow(typeof(StockSharp.PumpSwap.PumpSwapMessageAdapter))]
	[DataRow(typeof(StockSharp.Raydium.RaydiumMessageAdapter))]
	[DataRow(typeof(StockSharp.ManifestTrade.ManifestTradeMessageAdapter))]
	[DataRow(typeof(StockSharp.Orca.OrcaMessageAdapter))]
	[DataRow(typeof(StockSharp.Meteora.MeteoraMessageAdapter))]
	[DataRow(typeof(StockSharp.OSL.OSLMessageAdapter))]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task HeartbeatDoesNotReportUnsupported(Type adapterType)
	{
		using var adapter = (MessageAdapter)Activator.CreateInstance(adapterType, new IncrementalIdGenerator());
		var errors = new List<Exception>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			if (message is ErrorMessage error)
				errors.Add(error.Error);
			return default;
		};

		await adapter.SendInMessageAsync(new TimeMessage(), CancellationToken);

		AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
	}
}
