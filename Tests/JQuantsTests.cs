namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.JQuants;
using StockSharp.JQuants.Native;
using StockSharp.Messages;

[TestClass]
public class JQuantsTests : BaseTestClass
{
	private sealed class Handler(
		Func<HttpRequestMessage, CancellationToken,
			Task<HttpResponseMessage>> callback) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
			=> callback(request, cancellationToken);
	}

	[TestMethod]
	public void DefaultsAndSettingsUseV2Endpoint()
	{
		var source = new JQuantsMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.jquants.com/v2",
			source.RestEndpoint);
		AreEqual(TimeSpan.FromSeconds(12),
			source.RequestInterval);
		AreEqual(1000, source.MaximumPages);
		AreEqual(6, JQuantsMessageAdapter.AllTimeFrames.Count());
		CollectionAssert.AreEqual(new[] { BoardCodes.Tse },
			source.AssociatedBoards);

		source.Key = "api-key".Secure();
		source.RestEndpoint = "https://api.example/v2";
		source.RequestInterval = TimeSpan.FromMilliseconds(250);
		source.MaximumPages = 25;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new JQuantsMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("api-key", target.Key.UnSecure());
		AreEqual("https://api.example/v2",
			target.RestEndpoint);
		AreEqual(TimeSpan.FromMilliseconds(250),
			target.RequestInterval);
		AreEqual(25, target.MaximumPages);
	}

	[TestMethod]
	public async Task RestClientUsesApiKeyAndFollowsPagination()
	{
		var requests = new List<string>();
		var handler = new Handler((request, _) =>
		{
			requests.Add(request.RequestUri.PathAndQuery);
			AreEqual("api-key", request.Headers.GetValues(
				"x-api-key").Single());
			return Task.FromResult(Json(
				request.RequestUri.Query.Contains(
					"pagination_key=next", StringComparison.Ordinal)
					? """{"data":[{"Code":"72030","CoName":"Toyota"}]}"""
					: """{"data":[{"Code":"86970","CoName":"JPX"}],"pagination_key":"next"}"""));
		});
		using var client = new JQuantsRestClient(
			"https://api.example/v2", "api-key".Secure(),
			TimeSpan.Zero, 10, handler);

		var values = await client.GetEquitiesAsync(
			null, new(2026, 7, 28), CancellationToken);

		AreEqual(2, values.Length);
		AreEqual("86970", values[0].Value<string>("Code"));
		AreEqual("72030", values[1].Value<string>("Code"));
		CollectionAssert.AreEqual(new[]
		{
			"/v2/equities/master?date=2026-07-28",
			"/v2/equities/master?date=2026-07-28&pagination_key=next",
		}, requests);
	}

	[TestMethod]
	public void EquityMasterMapsOfficialV2Fields()
	{
		var instrument = JObject.Parse(
			"""
			{
			  "Date": "2026-07-29",
			  "Code": "86970",
			  "CoName": "日本取引所グループ",
			  "CoNameEn": "Japan Exchange Group, Inc.",
			  "S17": "16",
			  "S17Nm": "金融（除く銀行）",
			  "S33": "7100",
			  "S33Nm": "その他金融業",
			  "Mkt": "0111",
			  "MktNm": "プライム"
			}
			""").ToEquity();

		AreEqual("86970", instrument.Code);
		AreEqual("日本取引所グループ", instrument.Name);
		AreEqual("Japan Exchange Group, Inc.",
			instrument.EnglishName);
		AreEqual("0111", instrument.Market);
		AreEqual("7100", instrument.Sector);
		AreEqual("E:86970", instrument.NativeId);
		AreEqual(JQuantsInstrumentKinds.Equity,
			instrument.Kind);
	}

	[TestMethod]
	public void MinuteBarsAggregateInJapanTime()
	{
		var rows = new[]
		{
			JObject.Parse(
				"""{"Date":"2026-07-28","Time":"09:00:00","Code":"86970","O":1500,"H":1510,"L":1498,"C":1505,"Vo":100}"""),
			JObject.Parse(
				"""{"Date":"2026-07-28","Time":"09:01:00","Code":"86970","O":1505,"H":1515,"L":1502,"C":1512,"Vo":200}"""),
			JObject.Parse(
				"""{"Date":"2026-07-28","Time":"09:05:00","Code":"86970","O":1512,"H":1520,"L":1510,"C":1518,"Vo":300}"""),
		};

		var bars = JQuantsExtensions.Aggregate(
			rows.Select(static row => row.ToBar(true)),
			TimeSpan.FromMinutes(5));

		AreEqual(2, bars.Length);
		AreEqual(new DateTimeOffset(2026, 7, 28, 9, 0, 0,
			TimeSpan.FromHours(9)), bars[0].Time);
		AreEqual(1500m, bars[0].Open);
		AreEqual(1515m, bars[0].High);
		AreEqual(1498m, bars[0].Low);
		AreEqual(1512m, bars[0].Close);
		AreEqual(300m, bars[0].Volume);
		AreEqual(1518m, bars[1].Close);
	}

	[TestMethod]
	public void DerivativesMapV2ProductAndContractFields()
	{
		var future = JObject.Parse(
			"""
			{
			  "Code": "169120018",
			  "ProdCat": "Nikkei 225 Futures",
			  "LTD": "2026-09-10",
			  "O": "40000",
			  "H": "40500",
			  "L": "39800",
			  "C": "40300",
			  "Vo": "1234",
			  "OI": "5678",
			  "Date": "2026-07-28"
			}
			""");
		var option = JObject.Parse(
			"""
			{
			  "Code": "139120018",
			  "ProdCat": "Nikkei 225 Options",
			  "UndSSO": "N225",
			  "PCDiv": "2",
			  "Strike": "40000",
			  "LTD": "2026-09-10"
			}
			""").ToDerivative(JQuantsInstrumentKinds.Option);
		var futureInstrument = future.ToDerivative(
			JQuantsInstrumentKinds.Future);
		var bar = future.ToBar(false);

		AreEqual("F:169120018", futureInstrument.NativeId);
		AreEqual(JQuantsInstrumentKinds.Future,
			futureInstrument.Kind);
		AreEqual("O:139120018", option.NativeId);
		AreEqual("N225", option.Underlying);
		AreEqual(OptionTypes.Put, option.OptionType);
		AreEqual(40000m, option.Strike);
		AreEqual(new DateTime(2026, 9, 10, 0, 0, 0,
			DateTimeKind.Utc), option.Expiry);
		AreEqual(40300m, bar.Close);
		AreEqual(5678m, bar.OpenInterest);
	}

	[TestMethod]
	public async Task TickBulkDownloadUsesSignedGzipCsv()
	{
		var requests = new List<string>();
		var csv =
			"Date,Time,Code,Price,Volume,SequentialTradeNumber\n" +
			"2026-07-28,09:00:00.123,86970,1500.5,100,42\n" +
			"2026-07-28,09:00:00.456,72030,3000,200,43\n";
		var gzip = Gzip(csv);
		var handler = new Handler((request, _) =>
		{
			requests.Add(request.RequestUri.AbsoluteUri);
			if (request.RequestUri.Host == "download.example")
			{
				IsFalse(request.Headers.Contains("x-api-key"));
				return Task.FromResult(new HttpResponseMessage(
					HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(gzip),
				});
			}
			AreEqual("api-key", request.Headers.GetValues(
				"x-api-key").Single());
			return Task.FromResult(Json(
				"""{"url":"https://download.example/trades.csv.gz"}"""));
		});
		using var client = new JQuantsRestClient(
			"https://api.example/v2", "api-key".Secure(),
			TimeSpan.Zero, 10, handler);

		var trades = await client.GetTradesAsync("86970",
			new(2026, 7, 28), CancellationToken);

		AreEqual(1, trades.Length);
		AreEqual("42", trades[0].Id);
		AreEqual(1500.5m, trades[0].Price);
		AreEqual(100m, trades[0].Volume);
		AreEqual(new DateTimeOffset(2026, 7, 28, 9, 0, 0,
			123, TimeSpan.FromHours(9)), trades[0].Time);
		IsTrue(requests[0].Contains(
			"/v2/bulk/get?endpoint=%2Fequities%2Ftrades&date=2026-07-28",
			StringComparison.Ordinal));
		AreEqual("https://download.example/trades.csv.gz",
			requests[1]);
	}

	private static byte[] Gzip(string value)
	{
		using var target = new MemoryStream();
		using (var gzip = new GZipStream(target,
			CompressionLevel.SmallestSize, true))
		{
			var bytes = Encoding.UTF8.GetBytes(value);
			gzip.Write(bytes, 0, bytes.Length);
		}
		return target.ToArray();
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
