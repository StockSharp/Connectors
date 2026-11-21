namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.Messages;
using StockSharp.QFEX;

/// <summary>
/// QFEX publishes market data without credentials.
/// </summary>
[TestClass]
public class QFEXMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void ReferenceDataRecognizesRiskReduceOnlyStatus()
	{
		var responseType = typeof(QFEXMessageAdapter).Assembly.GetType("StockSharp.QFEX.Native.QFEXReferenceDataResponse", throwOnError: true);
		var response = JsonConvert.DeserializeObject("""{"data":[{"status":"RISK_REDUCE_ONLY"},{"status":"ACTIVE"}]}""", responseType);
		var data = (Array)responseType.GetProperty("Data").GetValue(response);
		var restricted = data.GetValue(0);
		var status = restricted.GetType().GetProperty("Status");

		AreEqual("\"RISK_REDUCE_ONLY\"", JsonConvert.SerializeObject(status.GetValue(restricted)));
		AreEqual("\"ACTIVE\"", JsonConvert.SerializeObject(status.GetValue(data.GetValue(1))));
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new QFEXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "NVDA-USD",
		BoardCode = BoardCodes.QFEX,
	};

	/// <inheritdoc />
	/// <remarks>
	/// The trade stream works, but the venue is far too thin to produce a print in
	/// time: a session subscribed to every active QFEX symbol at once saw three
	/// trades in five minutes, and none of them on this instrument.
	/// </remarks>
	protected override DataType[] SkippedDataTypes => [DataType.Ticks];
}
