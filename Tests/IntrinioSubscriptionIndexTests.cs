namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Intrinio.Native;

[TestClass]
public class IntrinioSubscriptionIndexTests : BaseTestClass
{
	[TestMethod]
	public void IndexesSubscriptionsByFeedAndTransaction()
	{
		var index = new IntrinioSubscriptionIndex();
		var firstEquity = Create(1, "AAPL", false);
		var secondEquity = Create(2, "aapl", false);
		var option = Create(3, "AAPL", true);

		IsTrue(index.TryAdd(firstEquity, out var isFirst));
		IsTrue(isFirst);
		IsTrue(index.TryAdd(secondEquity, out isFirst));
		IsTrue(!isFirst);
		IsTrue(index.TryAdd(option, out isFirst));
		IsTrue(isFirst);
		IsTrue(!index.TryAdd(Create(1, "MSFT", false), out _));

		AreEqual(2, index.Match(false, "AaPl").Length);
		AreEqual(1, index.Match(true, "aapl").Length);
		AreEqual(0, index.Match(false, "MSFT").Length);
		IsTrue(index.TryGet(1, out var found, out var isLast));
		AreEqual(firstEquity, found);
		IsTrue(!isLast);
		IsTrue(index.TryGet(3, out found, out isLast));
		AreEqual(option, found);
		IsTrue(isLast);

		IsTrue(index.TryRemove(1, out var removed, out isLast));
		AreEqual(firstEquity, removed);
		IsTrue(!isLast);
		AreEqual(1, index.Match(false, "AAPL").Length);

		IsTrue(index.TryRemove(2, out removed, out isLast));
		AreEqual(secondEquity, removed);
		IsTrue(isLast);
		AreEqual(0, index.Match(false, "AAPL").Length);
	}

	[TestMethod]
	public async Task DeactivationWaitsForInFlightDelivery()
	{
		var subscription = Create(1, "AAPL", false);

		IsTrue(subscription.TryEnterDelivery());
		var deactivation = subscription.DeactivateAsync();
		IsTrue(!deactivation.IsCompleted);

		subscription.ExitDelivery();
		await deactivation;

		IsTrue(!subscription.TryEnterDelivery());
		subscription.Activate();
		IsTrue(subscription.TryEnterDelivery());
		subscription.ExitDelivery();
	}

	private static IntrinioStreamSubscription Create(long transactionId,
		string symbol, bool isOption)
		=> new()
		{
			TransactionId = transactionId,
			Symbol = symbol,
			IsOption = isOption,
		};
}
