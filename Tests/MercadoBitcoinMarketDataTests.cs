namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.MercadoBitcoin;
using StockSharp.Messages;

/// <summary>
/// MercadoBitcoin publishes market data without credentials.
/// </summary>
[TestClass]
public class MercadoBitcoinMarketDataTests : LiveMarketDataTestBase
{
	private protected override ValueTask<string> GetVenueUnavailableReasonAsync(Exception failure)
		=> ValueTask.FromResult(failure is InvalidOperationException &&
			failure.Message.StartsWith("Mercado Bitcoin HTTP 403:", StringComparison.Ordinal) &&
			failure.Message.Contains("Attention Required! | Cloudflare", StringComparison.Ordinal)
			? "Cloudflare refused access to Mercado Bitcoin's public API from this network (HTTP 403)."
			: null);

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MercadoBitcoinMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-BRL",
		BoardCode = BoardCodes.MercadoBitcoin,
	};
}
