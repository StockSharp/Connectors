namespace StockSharp.Connectors.Tests;

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;

[TestClass]
[CLSCompliant(false)]
public class LiveMarketDataValidationTests : BaseTestClass
{
	[TestMethod]
	[DataRow("connect")]
	[DataRow("lookup")]
	[DataRow("data")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ReportedFailuresRetainTheOriginalThrowSite(string operation)
	{
		var expected = CreateVenueError();
		Exception actual;
		if (operation == "data")
			actual = ThrowsExactly<InvalidOperationException>(() => new KucoinMarketDataTests()
				.CheckData(new MarketDataResult<ExecutionMessage>(1, expected, null), "ticks"));
		else
		{
			await using var harness = new MarketDataTestHarness(new FailureAdapter(operation, expected));
			actual = await ThrowsAsync<InvalidOperationException>(async () =>
			{
				await harness.ConnectAsync(TimeSpan.FromSeconds(2), CancellationToken);
				if (operation == "lookup")
					await harness.LookupSecuritiesAsync(1, TimeSpan.FromSeconds(2), CancellationToken);
			});
		}

		AreEqual(expected, actual);
		IsTrue(actual.StackTrace.Contains(nameof(CreateVenueError)), "The reported error lost the original failure location.");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Exception CreateVenueError()
	{
		try
		{
			throw new InvalidOperationException("The venue rejected the request.");
		}
		catch (Exception error)
		{
			return error;
		}
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task AConnectionFailureIncludesErrorsReportedBeforeTheFinalReply()
	{
		var expected = CreateVenueError();
		var diagnostic = new InvalidOperationException("The streaming receiver failed.");
		await using var harness = new MarketDataTestHarness(new FailureAdapter("connect", expected, diagnostic));

		var error = await ThrowsAsync<AggregateException>(() => harness.ConnectAsync(TimeSpan.FromSeconds(2), CancellationToken));

		AreEqual(2, error.InnerExceptions.Count);
		AreEqual(expected, error.InnerExceptions[0]);
		AreEqual(diagnostic, error.InnerExceptions[1]);
	}

	private sealed class FailureAdapter(string operation, Exception error, Exception diagnostic = null) : MessageAdapter(new IncrementalIdGenerator())
	{
		protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
		{
			if (diagnostic is not null)
				await SendOutErrorAsync(diagnostic, cancellationToken);
			await SendOutMessageAsync(new ConnectMessage { Error = operation == "connect" ? error : null }, cancellationToken);
		}

		protected override ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
			=> SendSubscriptionReplyAsync(message.TransactionId, cancellationToken, error);
	}

	[TestMethod]
	public void ARejectedAdvertisedSubscriptionFailsInsteadOfBeingSkipped()
	{
		var expected = new NotSupportedException("The venue rejected a supported subscription.");
		var actual = ThrowsExactly<NotSupportedException>(() => new KucoinMarketDataTests()
			.CheckData(new MarketDataResult<ExecutionMessage>(1, expected, null), "ticks"));

		AreEqual(expected, actual);
	}
}
