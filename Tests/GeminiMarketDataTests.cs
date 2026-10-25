namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;
using Moq.Protected;

using StockSharp.Gemini;
using StockSharp.Messages;

/// <summary>
/// Gemini publishes market data without credentials.
/// </summary>
[TestClass]
public class GeminiMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SecuritiesCheckLoadsOnlyTheSubscribedInstrumentDetails()
	{
		var requests = new ConcurrentQueue<string>();
		HistoryHttpServer http = null;
		await using (http = new HistoryHttpServer(() =>
		{
			var path = new Uri(http.Address, http.RequestTarget).AbsolutePath;
			requests.Enqueue(path);
			var body = path switch
			{
				"/v1/symbols" => """["aaausd","btcusd","ethusd"]""",
				"/v1/symbols/details/btcusd" => """{"symbol":"BTCUSD","base_currency":"BTC","quote_currency":"USD","tick_size":0.00000001,"quote_increment":0.01,"min_order_size":"0.00001","status":"open","product_type":"spot"}""",
				_ => null,
			};
			return new HttpResponseMessage(body is null ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
			{
				Content = new StringContent(body ?? """{"result":"error","reason":"UnexpectedSymbol","message":"The lookup requested an unrelated instrument."}"""),
			};
		}, CancellationToken))
		{
			using var socket = new WebSocketTestServer();
			var fixture = new Mock<GeminiMarketDataTests> { CallBase = true };
			fixture.Protected().Setup<MessageAdapter>("CreateAdapter").Returns(new GeminiMessageAdapter(new IncrementalIdGenerator())
			{
				RestEndpoint = http.Address.ToString(),
				WebSocketEndpoint = socket.Endpoint,
				HeartbeatInterval = TimeSpan.Zero,
			});
			fixture.Protected().SetupGet<TimeSpan>("ConnectTimeout").Returns(TimeSpan.FromSeconds(3));
			fixture.Protected().SetupGet<TimeSpan>("SecuritiesTimeout").Returns(TimeSpan.FromSeconds(3));
			fixture.Object.TestContext = TestContext;

			var accepting = socket.AcceptAsync(CancellationToken);
			var checking = fixture.Object.Securities();
			using var peer = await accepting;
			await checking;

			AreEqual(2, requests.Count);
			AreEqual("/v1/symbols", requests.ToArray()[0]);
			AreEqual("/v1/symbols/details/btcusd", requests.ToArray()[1]);
		}
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GeminiMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSD",
		BoardCode = BoardCodes.Gemini,
	};

	/// <inheritdoc />
	/// <remarks>Gemini requires a separate HTTP request for each instrument's details.</remarks>
	protected override SecurityLookupMessage SecuritiesLookup => new() { SecurityId = TestSecurityId };
}
