namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
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
using StockSharp.OpenFigi;
using StockSharp.OpenFigi.Native;

[TestClass]
public class OpenFigiTests : BaseTestClass
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
		var source = new OpenFigiMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(new Uri("https://api.openfigi.com/v3/"),
			source.RestEndpoint);
		AreEqual(TimeSpan.FromSeconds(12),
			source.RequestInterval);
		AreEqual(150, source.MaximumPages);
		AreEqual(15000, source.MaximumResults);
		IsFalse(source.IsAllDownloadingSupported(
			DataType.Securities));

		source.Key = "secret".Secure();
		source.RestEndpoint = new("https://figi.example/v3/");
		source.RequestInterval = TimeSpan.FromSeconds(1);
		source.MaximumPages = 10;
		source.MaximumResults = 500;
		source.ExchangeCode = "US";
		source.MicCode = "XNYS";
		source.Currency = "USD";
		source.MarketSector = "Equity";
		source.SecurityType2 = "Common Stock";
		source.IncludeUnlistedEquities = true;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new OpenFigiMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("secret", target.Key.UnSecure());
		AreEqual(source.RestEndpoint, target.RestEndpoint);
		AreEqual(source.RequestInterval, target.RequestInterval);
		AreEqual(10, target.MaximumPages);
		AreEqual(500, target.MaximumResults);
		AreEqual("US", target.ExchangeCode);
		AreEqual("XNYS", target.MicCode);
		AreEqual("USD", target.Currency);
		AreEqual("Equity", target.MarketSector);
		AreEqual("Common Stock", target.SecurityType2);
		IsTrue(target.IncludeUnlistedEquities);
	}

	[TestMethod]
	public async Task MappingUsesV3HostKeyAndArrayBody()
	{
		Uri address = null;
		string key = null;
		string body = null;
		var handler = new Handler(async (request, cancellationToken) =>
		{
			address = request.RequestUri;
			key = request.Headers.GetValues(
				"X-OPENFIGI-APIKEY").Single();
			body = await request.Content.ReadAsStringAsync(
				cancellationToken);
			return Json(
				"""[{"data":[{"figi":"BBG000BLNNH6","ticker":"IBM"}]}]""");
		});
		using var client = new OpenFigiRestClient(
			new("https://figi.example/v3/"), "secret".Secure(),
			TimeSpan.Zero, handler);

		var result = await client.MapAsync(new JObject
		{
			["idType"] = "ID_BB_GLOBAL",
			["idValue"] = "BBG000BLNNH6",
		}, CancellationToken);

		AreEqual("https://figi.example/v3/mapping",
			address.AbsoluteUri);
		AreEqual("secret", key);
		AreEqual(1, JArray.Parse(body).Count);
		AreEqual("BBG000BLNNH6", result.Single().Figi);
	}

	[TestMethod]
	public async Task MappingDistinguishesWarningAndError()
	{
		var responses = new Queue<string>(
		[
			"""[{"warning":"No identifier found."}]""",
			"""[{"error":"Invalid idType."}]""",
		]);
		var handler = new Handler((_, _) => Task.FromResult(
			Json(responses.Dequeue())));
		using var client = new OpenFigiRestClient(
			new("https://figi.example/v3/"), null,
			TimeSpan.Zero, handler);
		var request = new JObject
		{
			["idType"] = "ID_ISIN",
			["idValue"] = "missing",
		};

		var missing = await client.MapAsync(request,
			CancellationToken);
		var error = await ThrowsExactlyAsync<
			InvalidOperationException>(() =>
				client.MapAsync(request, CancellationToken).AsTask());

		AreEqual(0, missing.Length);
		AreEqual("Invalid idType.", error.Message);
	}

	[TestMethod]
	public async Task SearchFollowsOpaqueCursor()
	{
		var bodies = new List<JObject>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			bodies.Add(JObject.Parse(
				await request.Content.ReadAsStringAsync(
					cancellationToken)));
			return bodies.Count == 1
				? Json(
					"""{"data":[{"figi":"ONE","ticker":"ONE"}],"next":"opaque-token","total":2}""")
				: Json(
					"""{"data":[{"figi":"TWO","ticker":"TWO"}],"total":2}""");
		});
		using var client = new OpenFigiRestClient(
			new("https://figi.example/v3/"), null,
			TimeSpan.Zero, handler);

		var result = await client.SearchAsync(new JObject
		{
			["query"] = "example",
		}, true, 150, 15000, CancellationToken);

		AreEqual(2, result.Length);
		AreEqual(2, bodies.Count);
		IsNull(bodies[0]["start"]);
		AreEqual("opaque-token",
			bodies[1].Value<string>("start"));
	}

	[TestMethod]
	public void OfficialV3FieldsMapToSecurity()
	{
		var instrument = JObject.Parse(
			"""
			{
			  "figi":"BBG000BLNNH6",
			  "name":"INTL BUSINESS MACHINES CORP",
			  "ticker":"IBM",
			  "exchCode":"US",
			  "compositeFIGI":"BBG000BLNNH6",
			  "securityType":"Common Stock",
			  "marketSector":"Equity",
			  "shareClassFIGI":"BBG001S5S399",
			  "securityType2":"Common Stock",
			  "securityDescription":"IBM"
			}
			""").ToObject<OpenFigiInstrument>();

		var security = instrument.ToSecurityMessage(42,
			"ID_ISIN", "US4592001014", "USD");

		AreEqual(42, security.OriginalTransactionId);
		AreEqual("IBM", security.SecurityId.SecurityCode);
		AreEqual(BoardCodes.OpenFigi,
			security.SecurityId.BoardCode);
		AreEqual("BBG000BLNNH6", security.SecurityId.Native);
		AreEqual("BBG000BLNNH6",
			security.SecurityId.Bloomberg);
		AreEqual("US4592001014", security.SecurityId.Isin);
		AreEqual(SecurityTypes.Stock, security.SecurityType);
		AreEqual(CurrencyTypes.USD, security.Currency);
		AreEqual("Equity/Common Stock", security.Class);
	}

	[TestMethod]
	public void LookupInfersIdentifiersAndAppliesFilters()
	{
		var adapter = new OpenFigiMessageAdapter(
			new IncrementalIdGenerator())
		{
			ExchangeCode = "US",
			MicCode = "XNYS",
			Currency = "USD",
			MarketSector = "Equity",
			SecurityType2 = "Common Stock",
			IncludeUnlistedEquities = true,
		};

		var mapping = adapter.CreateRequest(
			new SecurityLookupMessage
			{
				SecurityId = new()
				{
					Isin = "US4592001014",
				},
			});
		var search = adapter.CreateRequest(
			new SecurityLookupMessage
			{
				Name = "International Business Machines",
			});

		AreEqual("ID_ISIN",
			mapping.Mapping.Value<string>("idType"));
		AreEqual("US4592001014",
			mapping.Mapping.Value<string>("idValue"));
		AreEqual("XNYS",
			mapping.Mapping.Value<string>("micCode"));
		IsTrue(mapping.Mapping.Value<bool>(
			"includeUnlistedEquities"));
		IsTrue(search.UseSearch);
		AreEqual("International Business Machines",
			search.Criteria.Value<string>("query"));
		AreEqual("Common Stock",
			search.Criteria.Value<string>("securityType2"));
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
