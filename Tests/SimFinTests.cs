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
using StockSharp.SimFin;
using StockSharp.SimFin.Native;

[TestClass]
public class SimFinTests : BaseTestClass
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
	public void DefaultsAndSettingsRoundTrip()
	{
		var source = new SimFinMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(new Uri("https://prod.simfin.com/api/v3/"),
			source.RestEndpoint);
		AreEqual(TimeSpan.FromMilliseconds(500),
			source.RequestInterval);
		AreEqual("pl,bs,cf,derived", source.StatementTypes);
		AreEqual("fy", source.Period);
		AreEqual(100000, source.MaximumRecords);
		IsTrue(source.IsAllDownloadingSupported(
			DataType.Securities));

		source.Key = "secret".Secure();
		source.RestEndpoint = new("https://simfin.example/v3/");
		source.RequestInterval = TimeSpan.FromSeconds(1);
		source.StatementTypes = "pl,derived";
		source.Period = "q1,q2,q3,q4";
		source.AsReported = true;
		source.IncludeRatios = true;
		source.MaximumRecords = 500;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new SimFinMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("secret", target.Key.UnSecure());
		AreEqual(source.RestEndpoint, target.RestEndpoint);
		AreEqual(source.RequestInterval, target.RequestInterval);
		AreEqual("pl,derived", target.StatementTypes);
		AreEqual("q1,q2,q3,q4", target.Period);
		IsTrue(target.AsReported);
		IsTrue(target.IncludeRatios);
		AreEqual(500, target.MaximumRecords);
	}

	[TestMethod]
	public async Task ClientUsesConfiguredHostAuthorizationAndPriceQuery()
	{
		HttpRequestMessage captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request;
			return Task.FromResult(Json("[]"));
		});
		using var client = new SimFinRestClient(
			new("https://simfin.example/api/v3/"),
			"secret".Secure(), TimeSpan.Zero, handler);

		await client.GetPricesAsync("AAPL",
			new DateTime(2026, 1, 1),
			new DateTime(2026, 1, 31), true, false,
			CancellationToken);

		AreEqual("secret",
			captured.Headers.GetValues("Authorization").Single());
		AreEqual(
			"https://simfin.example/api/v3/companies/prices/compact" +
				"?ticker=AAPL&ratios=true&asreported=false" +
				"&start=2026-01-01&end=2026-01-31",
			captured.RequestUri.AbsoluteUri);
	}

	[TestMethod]
	public void CompanyListSupportsObjectAndCompactShapes()
	{
		var objects = JArray.Parse(
			"""
			[
			  {
			    "id":111052,
			    "name":"Apple Inc.",
			    "ticker":"AAPL",
			    "isin":"US0378331005",
			    "sectorCode":1010,
			    "sectorName":"Technology",
			    "industryName":"Consumer Electronics",
			    "market":"US"
			  }
			]
			""").ToCompanies();
		var compact = JObject.Parse(
			"""
			{
			  "columns":["id","name","ticker","isin","market"],
			  "data":[[111052,"Apple Inc.","AAPL","US0378331005","US"]]
			}
			""").ToCompanies();

		AreEqual(1, objects.Length);
		AreEqual("AAPL", objects[0].Ticker);
		AreEqual("Consumer Electronics",
			objects[0].IndustryName);
		AreEqual("US0378331005",
			objects[0].ToSecurityId().Isin);
		AreEqual(BoardCodes.SimFin,
			objects[0].ToSecurityId().BoardCode);
		AreEqual(objects[0].Id, compact[0].Id);
	}

	[TestMethod]
	public void CompactPricesMapByColumnName()
	{
		var prices = JArray.Parse(
			"""
			[
			  {
			    "id":111052,
			    "name":"Apple Inc.",
			    "ticker":"AAPL",
			    "currency":"USD",
			    "columns":[
			      "Date","Open","High","Low","Close","Adj. Close",
			      "Volume","Restated"
			    ],
			    "data":[
			      ["2026-01-02",250.0,253.0,249.0,252.0,252.0,50000000,false]
			    ]
			  }
			]
			""").ToPrices();

		AreEqual(1, prices.Length);
		AreEqual(new DateTime(2026, 1, 2, 0, 0, 0,
			DateTimeKind.Utc), prices[0].Date);
		AreEqual(250m, prices[0].Open);
		AreEqual(253m, prices[0].High);
		AreEqual(249m, prices[0].Low);
		AreEqual(252m, prices[0].AdjustedClose);
		AreEqual(50000000m, prices[0].Volume);
	}

	[TestMethod]
	public void CompactStatementsFlattenMetrics()
	{
		var fundamentals = JArray.Parse(
			"""
			[
			  {
			    "id":111052,
			    "ticker":"AAPL",
			    "currency":"USD",
			    "statements":[
			      {
			        "statement":"pl",
			        "columns":[
			          "Fiscal Year","Fiscal Period","Report Date",
			          "Publish Date","Restated","Revenue","Net Income"
			        ],
			        "data":[[
			          [2025,"FY","2025-09-27","2025-10-31",false,
			           416161000000,112010000000]
			        ]]
			      }
			    ]
			  }
			]
			""").ToFundamentals();

		AreEqual(2, fundamentals.Length);
		var revenue = fundamentals.Single(
			item => item.Metric == "Revenue");
		AreEqual("pl", revenue.Statement);
		AreEqual(2025, revenue.FiscalYear);
		AreEqual("FY", revenue.FiscalPeriod);
		AreEqual(416161000000m, revenue.Value);
		AreEqual("USD", revenue.Currency);
	}

	[TestMethod]
	public void FundamentalMessagePreservesTypedDataOnClone()
	{
		var source = new SimFinFundamentalMessage
		{
			SecurityId = new()
			{
				SecurityCode = "AAPL",
				BoardCode = BoardCodes.SimFin,
				Native = 111052L,
			},
			ServerTime = new DateTime(2025, 10, 31, 0, 0, 0,
				DateTimeKind.Utc),
			Statement = "pl",
			Metric = "Revenue",
			RawValue = "416161000000",
			Value = 416161000000m,
			Currency = "USD",
			FiscalYear = 2025,
			FiscalPeriod = "FY",
			Restated = false,
		};

		var clone = (SimFinFundamentalMessage)source.Clone();

		AreEqual(SimFinDataTypes.Fundamentals, clone.DataType);
		AreEqual(source.SecurityId, clone.SecurityId);
		AreEqual("Revenue", clone.Metric);
		AreEqual(416161000000m, clone.Value);
		AreEqual(2025, clone.FiscalYear);
		IsFalse(clone.Restated.Value);
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
