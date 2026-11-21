namespace StockSharp.Connectors.Tests;

using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BTSE;
using StockSharp.Messages;

/// <summary>
/// BTSE publishes market data without credentials.
/// </summary>
[TestClass]
public class BTSEMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[CLSCompliant(false)]
	[DataRow(BTSESections.Spot, false, 1000, 500)]
	[DataRow(BTSESections.Spot, true, 1000, 500)]
	[DataRow(BTSESections.Spot, false, 200, 200)]
	[DataRow(BTSESections.Futures, false, 1000, 500)]
	[DataRow(BTSESections.Futures, true, 1000, 500)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TradeRequestsRespectTheVenueCountLimit(BTSESections section, bool hasStartTime, int count, int expectedCount)
	{
		using var adapter = new BTSEMessageAdapter(new IncrementalIdGenerator()) { Sections = [section] };
		var isSpot = section == BTSESections.Spot;
		var segment = isSpot ? "spot" : "futures";
		var securityId = isSpot ? TestSecurityId : new SecurityId { SecurityCode = "BTC-PERP", BoardCode = BoardCodes.BtseFutures };
		const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
		var restField = typeof(BTSEMessageAdapter).GetField($"_{segment}RestClient", fields);
		using var rest = (IDisposable)Activator.CreateInstance(restField.FieldType,
			$"https://example.test/{segment}", section, null, null);
		restField.SetValue(adapter, rest);
		var requests = 0;
		Uri requestUri = null;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			requestUri = request.RequestUri;
			requests++;
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
		}));
		HistoryHttpHandler.ReplaceClient(rest, http, "_http");
		var marketField = typeof(BTSEMessageAdapter).GetField($"_{segment}Markets", fields);
		var markets = (IDictionary)marketField.GetValue(adapter);
		markets.Add(securityId.SecurityCode, Activator.CreateInstance(marketField.FieldType.GetGenericArguments()[1]));
		var to = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = 42,
			IsSubscribe = true,
			SecurityId = securityId,
			DataType2 = DataType.Ticks,
			From = hasStartTime ? to.AddDays(-1) : null,
			To = to,
			Count = count,
		}, CancellationToken);

		AreEqual(1, requests);
		AreEqual(isSpot ? "/spot/api/v3.3/trades" : "/futures/api/v2.3/trades", requestUri.AbsolutePath);
		var countParameter = requestUri.Query.TrimStart('?').Split('&')
			.Single(parameter => parameter.StartsWith("count=", StringComparison.Ordinal));
		AreEqual($"count={expectedCount}", countParameter);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BTSEMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BTSESections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USD",
		BoardCode = BoardCodes.Btse,
	};
}
