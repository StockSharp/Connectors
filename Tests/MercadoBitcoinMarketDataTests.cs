namespace StockSharp.Connectors.Tests;

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
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MercadoBitcoinMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-BRL",
		BoardCode = BoardCodes.MercadoBitcoin,
	};
}
