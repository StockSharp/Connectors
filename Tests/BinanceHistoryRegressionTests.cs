namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BinanceHistory;
using StockSharp.Messages;

[TestClass]
[CLSCompliant(false)]
public class BinanceHistoryRegressionTests : HistoryTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task SpotLookupUsesPublicMarketDataEndpoint()
	{
		using var http = new HttpClient(new HistoryHttpHandler(request =>
			request.RequestUri.Host == "data-api.binance.vision" &&
			request.RequestUri.AbsolutePath == "/api/v3/exchangeInfo"
				? new(HttpStatusCode.OK)
				{
					Content = new StringContent("""{"symbols":[{"symbol":"BTCUSDT","baseAsset":"BTC","quoteAssetPrecision":8}]}"""),
				}
				: new((HttpStatusCode)451)));
		var adapter = CreateAdapter<BinanceHistoryMessageAdapter>();
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<BinanceHistoryMessageAdapter>(), http);

		var securities = await DownloadSecurities(adapter, new()
		{
			SecurityType = SecurityTypes.CryptoCurrency,
			Count = 1,
		}).ToArrayAsync(CancellationToken);

		AreEqual(1, securities.Length);
		AreEqual("BTCUSDT", securities[0].SecurityId.SecurityCode);
		AreEqual(BoardCodes.Binance, securities[0].SecurityId.BoardCode);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LookupPropagatesHttpFailure()
	{
		var adapter = CreateAdapter<BinanceHistoryMessageAdapter>();
		using var http = new HttpClient(new HistoryHttpHandler(_ => new((HttpStatusCode)451)
		{
			Content = new StringContent("Unavailable from this location."),
		}));
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<BinanceHistoryMessageAdapter>(), http);

		await ThrowsAsync<HttpRequestException>(async () =>
		{
			await foreach (var _ in DownloadSecurities(adapter, LookupAll).WithCancellation(CancellationToken))
			{
			}
		});
	}

	[TestMethod]
	[DataRow(false, false)]
	[DataRow(true, false)]
	[DataRow(false, true)]
	[DataRow(true, true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task DepthBandsBecomeSeparatePriceBuckets(bool inverse, bool fractionalBands)
	{
		var csv = inverse
			? "timestamp,percentage,depth,notional\n2024-01-01 00:00:00,-2,30,12.95\n2024-01-01 00:00:00,-1,10,3.7\n2024-01-01 00:00:00,1,10,2.96\n2024-01-01 00:00:00,2,30,7.96\n"
			: "timestamp,percentage,depth,notional\n2024-01-01 00:00:00,-2,30,2600\n2024-01-01 00:00:00,-1,10,1000\n2024-01-01 00:00:00,1,10,1250\n2024-01-01 00:00:00,2,30,4210\n";
		if (fractionalBands)
			csv = csv.Replace(",-2,", ",-0.2,").Replace(",-1,", ",-0.1,").Replace(",1,", ",0.1,").Replace(",2,", ",0.2,");

		var books = await ReadDepth(inverse, csv);

		AreEqual(1, books.Length);
		AreEqual(100m, books[0].Bids[0].Price);
		AreEqual(80m, books[0].Bids[1].Price);
		AreEqual(125m, books[0].Asks[0].Price);
		AreEqual(148m, books[0].Asks[1].Price);
		AreEqual(10m, books[0].Bids[0].Volume);
		AreEqual(20m, books[0].Bids[1].Volume);
		IsTrue(books[0].Bids.All(q => q.StartPosition is null));
	}

	[TestMethod]
	[DataRow("-1,10,1000\n2024-01-01 00:00:00,-2,5,1500\n", "Invalid cumulative depth")]
	[DataRow("-1,10,1000\n2024-01-01 00:00:00,1,10,500\n", "Crossed depth bands")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task InvalidArchiveDepthIsRejected(string rows, string expectedError)
	{
		var csv = "timestamp,percentage,depth,notional\n2024-01-01 00:00:00," + rows;
		var error = await ThrowsAsync<InvalidDataException>(() => ReadDepth(false, csv));
		IsTrue(error.Message.Contains(expectedError));
	}

	private async Task<QuoteChangeMessage[]> ReadDepth(bool inverse, string csv)
	{
		using var zip = new MemoryStream();
		using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
		{
			using var writer = new StreamWriter(archive.CreateEntry("depth.csv").Open(), new UTF8Encoding(false));
			await writer.WriteAsync(csv);
		}

		using var http = new HttpClient(new HistoryHttpHandler(request => new(HttpStatusCode.OK)
		{
			Content = request.RequestUri.Host == "data.binance.vision"
				? new ByteArrayContent(zip.ToArray())
				: new StringContent("{\"symbols\":[{\"symbol\":\"BTCUSD_PERP\",\"contractSize\":37}]}"),
		}));
		var adapter = CreateAdapter<BinanceHistoryMessageAdapter>(a => a.CheckDates = false);
		HistoryHttpHandler.ReplaceClient(adapter.FindAdapter<BinanceHistoryMessageAdapter>(), http);
		var secId = new SecurityId
		{
			SecurityCode = inverse ? "BTCUSD_PERP" : "BTCUSDT",
			BoardCode = inverse ? BoardCodes.BinanceCoin : BoardCodes.BinanceFut,
		};
		var from = new DateTime(2024, 1, 1).UtcKind();
		return await DownloadOrderBook(adapter, secId, from, from.AddHours(1), 1, SecurityTypes.Future)
			.ToArrayAsync(CancellationToken);
	}
}
