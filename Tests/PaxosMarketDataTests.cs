namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Paxos;

/// <summary>
/// Paxos Smart Order Routing market data requires an authorized OAuth client.
/// </summary>
[TestClass]
public class PaxosMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
	{
		var clientId = Env("PAXOS_CLIENT_ID");
		var clientSecret = Env("PAXOS_CLIENT_SECRET");
		if (clientId.IsEmpty() || clientSecret.IsEmpty())
			Inconclusive("Set PAXOS_CLIENT_ID and PAXOS_CLIENT_SECRET for an account authorized to read Paxos market data.");

		return new PaxosMessageAdapter(new IncrementalIdGenerator())
		{
			Key = clientId.Secure(),
			Secret = clientSecret.Secure(),
			Scopes = "exchange:read_aggregated_marketdata_stream exchange:historical_prices",
		};
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSD",
		BoardCode = BoardCodes.Paxos,
	};

	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public async Task PublicSecurities()
	{
		using var adapter = new PaxosMessageAdapter(new IncrementalIdGenerator());
		adapter.RemoveTransactionalSupport();
		var count = 0;
		await foreach (var security in adapter.ConnectAndDownloadAsync<SecurityMessage>(new SecurityLookupMessage
		{
			SecurityId = TestSecurityId,
			Count = 1,
		}).WithCancellation(CancellationToken))
		{
			AreEqual(TestSecurityId, security.SecurityId);
			count++;
		}
		AreEqual(1, count);
	}
}
