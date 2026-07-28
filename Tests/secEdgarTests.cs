namespace StockSharp.Connectors.Tests;

using System;
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

using StockSharp.Messages;
using StockSharp.SecEdgar;
using StockSharp.SecEdgar.Native;

[TestClass]
public class SecEdgarTests : BaseTestClass
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
	public void DefaultsAndSettingsFollowSecPolicy()
	{
		var source = new SecEdgarMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(new Uri("https://data.sec.gov/"),
			source.DataEndpoint);
		AreEqual(new Uri("https://www.sec.gov/"),
			source.WebsiteEndpoint);
		AreEqual("StockSharp support@stocksharp.com",
			source.UserAgent);
		AreEqual(TimeSpan.FromMilliseconds(125),
			source.RequestInterval);
		AreEqual(20, source.MaximumHistoricalFiles);
		AreEqual(10000, source.MaximumFacts);
		IsTrue(source.IsAllDownloadingSupported(
			DataType.Securities));

		source.DataEndpoint = new("https://data.example/");
		source.WebsiteEndpoint = new("https://www.example/");
		source.UserAgent = "Example Corp api@example.com";
		source.RequestInterval = TimeSpan.FromMilliseconds(250);
		source.Forms = "10-K,10-Q";
		source.MaximumHistoricalFiles = 5;
		source.MaximumFacts = 500;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new SecEdgarMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual(source.DataEndpoint, target.DataEndpoint);
		AreEqual(source.WebsiteEndpoint, target.WebsiteEndpoint);
		AreEqual(source.UserAgent, target.UserAgent);
		AreEqual(source.RequestInterval, target.RequestInterval);
		AreEqual(source.Forms, target.Forms);
		AreEqual(5, target.MaximumHistoricalFiles);
		AreEqual(500, target.MaximumFacts);
	}

	[TestMethod]
	public async Task ClientUsesConfiguredHostAndIdentifyingUserAgent()
	{
		HttpRequestMessage captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request;
			return Task.FromResult(Json(
				"""{"fields":["cik","name","ticker","exchange"],"data":[]}"""));
		});
		using var client = new SecEdgarRestClient(
			new("https://data.example/api/"),
			new("https://www.example/root/"),
			"Example Corp api@example.com",
			TimeSpan.FromMilliseconds(100), handler);

		await client.GetTickersAsync(CancellationToken);

		AreEqual(
			"https://www.example/root/files/company_tickers_exchange.json",
			captured.RequestUri.AbsoluteUri);
		AreEqual("Example Corp api@example.com",
			captured.Headers.GetValues("User-Agent").Single());
	}

	[TestMethod]
	public void TickerExchangeRowsMapToSecurities()
	{
		var companies = JObject.Parse(
			"""
			{
			  "fields":["cik","name","ticker","exchange"],
			  "data":[
			    [320193,"Apple Inc.","AAPL","Nasdaq"],
			    [1067983,"Berkshire Hathaway Inc.","BRK-B","NYSE"],
			    [123,"Example Fund","EXF","NYSE Arca"]
			  ]
			}
			""").ToCompanies();

		AreEqual(3, companies.Length);
		AreEqual("CIK0000320193", companies[0].Cik);
		AreEqual(BoardCodes.Nasdaq,
			companies[0].ToSecurityId().BoardCode);
		AreEqual(BoardCodes.Nyse,
			companies[1].ToSecurityId().BoardCode);
		AreEqual(BoardCodes.Arca,
			companies[2].ToSecurityId().BoardCode);
		AreEqual("CIK0000320193", "320193".NormalizeCik());
		AreEqual("CIK0000320193",
			"CIK0000320193".NormalizeCik());
		IsNull("AAPL".NormalizeCik());
	}

	[TestMethod]
	public void CompactSubmissionsMapByColumn()
	{
		var filing = JObject.Parse(
			"""
			{
			  "accessionNumber":["0000320193-26-000013"],
			  "filingDate":["2026-05-01"],
			  "reportDate":["2026-03-28"],
			  "acceptanceDateTime":["2026-05-01T06:01:26.000Z"],
			  "form":["10-Q"],
			  "fileNumber":["001-36743"],
			  "items":[""],
			  "size":[123456],
			  "isXBRL":[1],
			  "isInlineXBRL":[1],
			  "primaryDocument":["aapl-20260328.htm"],
			  "primaryDocDescription":["10-Q"]
			}
			""").ToFilings("CIK0000320193", "Apple Inc.")
			.Single();

		AreEqual("10-Q", filing.Form);
		AreEqual(new DateTime(2026, 5, 1, 6, 1, 26,
			DateTimeKind.Utc), filing.AcceptanceDateTime);
		IsTrue(filing.IsXbrl);
		IsTrue(filing.IsInlineXbrl);
		AreEqual(
			"https://www.sec.gov/Archives/edgar/data/320193/" +
				"000032019326000013/aapl-20260328.htm",
			filing.ToArchiveUri(new("https://www.sec.gov/"))
				.AbsoluteUri);
	}

	[TestMethod]
	public void CompanyFactsFlattenTaxonomyConceptUnitAndObservation()
	{
		var facts = JObject.Parse(
			"""
			{
			  "cik":320193,
			  "entityName":"Apple Inc.",
			  "facts":{
			    "us-gaap":{
			      "Assets":{
			        "label":"Assets",
			        "description":"Total assets.",
			        "units":{
			          "USD":[
			            {
			              "end":"2026-03-28",
			              "val":371082000000,
			              "accn":"0000320193-26-000013",
			              "fy":2026,
			              "fp":"Q2",
			              "form":"10-Q",
			              "filed":"2026-05-01",
			              "frame":"CY2026Q1I"
			            }
			          ]
			        }
			      }
			    }
			  }
			}
			""").ToFacts();

		AreEqual(1, facts.Length);
		AreEqual("us-gaap", facts[0].Taxonomy);
		AreEqual("Assets", facts[0].Concept);
		AreEqual("USD", facts[0].Unit);
		AreEqual("371082000000", facts[0].Value);
		AreEqual(371082000000m, facts[0].NumericValue);
		AreEqual(new DateTime(2026, 5, 1, 0, 0, 0,
			DateTimeKind.Utc), facts[0].FiledDate);
		AreEqual("CY2026Q1I", facts[0].Frame);
	}

	[TestMethod]
	public void FactMessagePreservesTypedDataOnClone()
	{
		var source = new SecEdgarFactMessage
		{
			SecurityId = new()
			{
				SecurityCode = "AAPL",
				BoardCode = BoardCodes.Nasdaq,
				Native = "CIK0000320193",
			},
			ServerTime = new(2026, 5, 1, 0, 0, 0,
				DateTimeKind.Utc),
			Taxonomy = "us-gaap",
			Concept = "Assets",
			Unit = "USD",
			Value = "371082000000",
			NumericValue = 371082000000m,
			Form = "10-Q",
		};

		var clone = (SecEdgarFactMessage)source.Clone();

		AreEqual(SecEdgarDataTypes.CompanyFacts, clone.DataType);
		AreEqual(source.SecurityId, clone.SecurityId);
		AreEqual("Assets", clone.Concept);
		AreEqual(371082000000m, clone.NumericValue);
		AreEqual("10-Q", clone.Form);
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
