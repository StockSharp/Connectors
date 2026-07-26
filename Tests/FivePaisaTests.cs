namespace StockSharp.Connectors.Tests;

using System;
using System.Text;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.FivePaisa;
using StockSharp.FivePaisa.Native;
using StockSharp.Messages;

[TestClass]
public class FivePaisaTests : BaseTestClass
{
	[TestMethod]
	public void FeedEndpointsRoundTripThroughSettings()
	{
		var source = new FivePaisaMessageAdapter(new IncrementalIdGenerator())
		{
			FeedWebSocketEndpoint = "wss://feed.example.test/default",
			FeedWebSocketAEndpoint = "wss://feed.example.test/a",
			FeedWebSocketBEndpoint = "wss://feed.example.test/b",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new FivePaisaMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.FeedWebSocketEndpoint, target.FeedWebSocketEndpoint);
		AreEqual(source.FeedWebSocketAEndpoint, target.FeedWebSocketAEndpoint);
		AreEqual(source.FeedWebSocketBEndpoint, target.FeedWebSocketBEndpoint);
	}

	[TestMethod]
	public void FeedEndpointFollowsTokenRedirectServer()
	{
		const string defaultEndpoint = "wss://feed.example.test/default";
		const string endpointA = "wss://feed.example.test/a";
		const string endpointB = "wss://feed.example.test/b";

		AreEqual(endpointA, FivePaisaFeedClient.ResolveEndpoint(
			CreateToken("A"), defaultEndpoint, endpointA, endpointB));
		AreEqual(endpointB, FivePaisaFeedClient.ResolveEndpoint(
			CreateToken("b"), defaultEndpoint, endpointA, endpointB));
		AreEqual(defaultEndpoint, FivePaisaFeedClient.ResolveEndpoint(
			CreateToken("unknown"), defaultEndpoint, endpointA, endpointB));
	}

	[TestMethod]
	public void InvalidTokenUsesDefaultFeedEndpoint()
	{
		const string defaultEndpoint = "wss://feed.example.test/default";

		AreEqual(defaultEndpoint, FivePaisaFeedClient.ResolveEndpoint(
			"not-a-token", defaultEndpoint,
			"wss://feed.example.test/a",
			"wss://feed.example.test/b"));
	}

	private static string CreateToken(string redirectServer)
	{
		var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
			$"{{\"RedirectServer\":\"{redirectServer}\"}}"))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

		return $"header.{payload}.signature";
	}
}
