namespace StockSharp.Connectors.Tests;

using System;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;

[TestClass]
public class LiveMarketDataValidationTests : BaseTestClass
{
	[TestMethod]
	public void ARejectedAdvertisedSubscriptionFailsInsteadOfBeingSkipped()
	{
		var expected = new NotSupportedException("The venue rejected a supported subscription.");
		var actual = ThrowsExactly<NotSupportedException>(() => new BitmartMarketDataTests()
			.CheckData(new MarketDataResult<ExecutionMessage>(1, expected, null), "ticks"));

		AreEqual(expected, actual);
	}
}
