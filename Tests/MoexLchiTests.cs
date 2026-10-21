namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.MoexLchi;

/// <summary>Anonymous MOEX contest trade archives over FTP.</summary>
[TestClass]
[RegionalLiveTests]
public class MoexLchiTests : HistoryTestBase
{
	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task OrderLog()
	{
		var adapter = CreateAdapter<MoexLchiMessageAdapter>();
		var subscription = new MarketDataMessage
		{
			IsSubscribe = true,
			DataType2 = DataType.OrderLog,
			From = new DateTime(2023, 9, 20, 0, 0, 0, DateTimeKind.Utc),
			To = new DateTime(2023, 9, 21, 0, 0, 0, DateTimeKind.Utc),
			Count = 1,
		};
		var received = false;
		await foreach (var trade in Download<ExecutionMessage>(adapter, subscription).WithCancellation(CancellationToken))
		{
			AreEqual(DataType.OrderLog, trade.DataTypeEx);
			IsGreater(trade.TradePrice ?? 0, 0m);
			IsFalse(trade.SecurityId.SecurityCode.IsEmpty());
			received = true;
			break;
		}
		IsTrue(received, "The anonymous MOEX archive returned no trades.");
	}
}
