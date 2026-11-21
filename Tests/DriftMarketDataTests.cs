namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Drift;
using StockSharp.Messages;

/// <summary>
/// Drift publishes market data without credentials.
/// </summary>
[TestClass]
public class DriftMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow("gzip")]
	[DataRow("br")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CompressedTradeHistoryIsDecoded(string encoding)
	{
		using var body = new MemoryStream();
		using (Stream compressor = encoding == "gzip"
			? new GZipStream(body, CompressionMode.Compress, true)
			: new BrotliStream(body, CompressionMode.Compress, true))
			await compressor.WriteAsync("""{"success":true,"records":[{"ts":42}]}"""u8.ToArray(), CancellationToken);
		await using var server = new HistoryHttpServer(() =>
		{
			var content = new ByteArrayContent(body.ToArray());
			content.Headers.ContentEncoding.Add(encoding);
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
		}, CancellationToken);
		var clientType = typeof(DriftMessageAdapter).Assembly.GetType("StockSharp.Drift.Native.DriftRestClient", throwOnError: true);
		using var client = (IDisposable)Activator.CreateInstance(clientType, "https://data.example", "https://dlob.example");
		var dataClient = (HttpClient)clientType.GetField("_dataClient", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(client);
		dataClient.BaseAddress = server.Address;
		var operation = clientType.GetMethod("GetTradesAsync").Invoke(client, ["SOL-PERP", 1, CancellationToken]);
		var task = (Task)operation.GetType().GetMethod("AsTask").Invoke(operation, null);
		await task;

		AreEqual("/market/SOL-PERP/trades?limit=1", server.RequestTarget);
		var trades = (Array)task.GetType().GetProperty("Result").GetValue(task);
		AreEqual(1, trades.Length);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DriftMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-PERP",
		BoardCode = BoardCodes.Drift,
	};
}
