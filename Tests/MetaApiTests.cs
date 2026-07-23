namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.MetaApi;
using StockSharp.MetaApi.Native;
using StockSharp.MetaApi.Native.Model;

[TestClass]
public class MetaApiTests : BaseTestClass
{
	[TestMethod]
	public void SocketIoUrlUsesOfficialEngineIo3Protocol()
	{
		var uri = MetaApiSocketIoProtocol.CreateWebSocketUri(
			new("https://mt-client-api-v1.new-york-a.example.test"),
			"token +/=?", "client-id");

		AreEqual("wss", uri.Scheme);
		AreEqual("/ws/", uri.AbsolutePath);
		IsTrue(uri.Query.Contains("auth-token=token%20%2B%2F%3D%3F",
			StringComparison.Ordinal));
		IsTrue(uri.Query.Contains("clientId=client-id", StringComparison.Ordinal));
		IsTrue(uri.Query.Contains("protocol=3", StringComparison.Ordinal));
		IsTrue(uri.Query.Contains("EIO=3", StringComparison.Ordinal));
		IsTrue(uri.Query.Contains("transport=websocket", StringComparison.Ordinal));
	}

	[TestMethod]
	public void SocketIoEventsRoundTripWithoutJsonQuotingTheFrame()
	{
		var frame = MetaApiSocketIoProtocol.EncodeEvent("synchronization",
			new MetaApiSynchronizationPacket
		{
			Type = "orders",
			AccountId = "account-1",
		});

		IsTrue(frame.StartsWith("42[", StringComparison.Ordinal));
		IsTrue(MetaApiSocketIoProtocol.TryParseEvent(frame, out var socketEvent));
		AreEqual("synchronization", socketEvent.Name);
		var payload = socketEvent.Synchronization;
		IsNotNull(payload);
		AreEqual("orders", payload.Type);
		AreEqual("account-1", payload.AccountId);
	}

	[TestMethod]
	public void TradeRequestsUseProtocolPropertyNames()
	{
		var json = MetaApiRestClient.SerializeBody(new MetaApiTradeRequest
		{
			ActionType = "ORDER_TYPE_BUY_LIMIT",
			Symbol = "EURUSD",
			Volume = 1m,
			OpenPrice = 1.1m,
			Expiration = new()
			{
				Type = "ORDER_TIME_SPECIFIED",
				Time = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc),
			},
		});

		IsTrue(json.Contains("\"actionType\":\"ORDER_TYPE_BUY_LIMIT\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"symbol\":\"EURUSD\"", StringComparison.Ordinal));
		IsTrue(json.Contains(
			"\"expiration\":{\"type\":\"ORDER_TIME_SPECIFIED\",\"time\":",
			StringComparison.Ordinal));
		IsFalse(json.Contains("\"ActionType\"", StringComparison.Ordinal));
		IsFalse(json.Contains("\"timeInForce\"", StringComparison.Ordinal));
		IsFalse(json.Contains("\"stopLoss\"", StringComparison.Ordinal));
	}

	[TestMethod]
	public void InitialSynchronizationRequiresBothHistoryMarkers()
	{
		var state = new MetaApiSynchronizationState();
		state.Begin("sync-1");

		IsTrue(state.TryAccept(Packet("orderSynchronizationFinished", "sync-1", 10)));
		IsFalse(state.IsReady);
		IsTrue(state.TryAccept(Packet("dealSynchronizationFinished", "sync-1", 11)));
		IsTrue(state.IsReady);
	}

	[TestMethod]
	public void SequenceGapRequestsACompleteResynchronization()
	{
		var state = new MetaApiSynchronizationState();
		state.Begin("sync-1");

		IsTrue(state.TryAccept(Packet("positions", "sync-1", 20)));
		IsFalse(state.TryAccept(Packet("orders", "sync-1", 20)));
		IsFalse(state.RequiresResynchronization);
		IsFalse(state.TryAccept(Packet("update", null, 22)));
		IsTrue(state.RequiresResynchronization);
	}

	[TestMethod]
	public void SymbolSpecificationMapsForexTradingRules()
	{
		var specification = new MetaApiSymbolSpecification
		{
			Symbol = "EURUSD",
			Description = "Euro vs US Dollar",
			Path = "Forex\\Majors\\EURUSD",
			BaseCurrency = "EUR",
			ProfitCurrency = "USD",
			TickSize = 0.00001m,
			MinVolume = 0.01m,
			MaxVolume = 100m,
			VolumeStep = 0.01m,
			ContractSize = 100000m,
			Digits = 5,
		};

		var security = specification.ToSecurityMessage(42);

		AreEqual(SecurityTypes.Currency, security.SecurityType);
		AreEqual("EURUSD", security.SecurityId.SecurityCode);
		AreEqual(MetaApiExtensions.BoardCode, security.SecurityId.BoardCode);
		AreEqual(CurrencyTypes.USD, security.Currency);
		AreEqual(0.00001m, security.PriceStep);
		AreEqual(0.01m, security.VolumeStep);
		AreEqual(100000m, security.Multiplier);
	}

	[TestMethod]
	public void ConditionalOrderMapsToMetaTraderStopCommand()
	{
		var message = new OrderRegisterMessage
		{
			TransactionId = 7,
			SecurityId = new() { SecurityCode = "EURUSD" },
			PortfolioName = "account-1",
			Side = Sides.Buy,
			OrderType = OrderTypes.Conditional,
			Volume = 2m,
			Condition = new MetaApiOrderCondition
			{
				ActivationPrice = 1.09m,
				StopLoss = 1.08m,
				TakeProfit = 1.11m,
			},
		};

		var request = message.ToTradeRequest();

		AreEqual("ORDER_TYPE_BUY_STOP", request.ActionType);
		AreEqual("EURUSD", request.Symbol);
		AreEqual(2m, request.Volume);
		AreEqual(1.09m, request.OpenPrice);
		AreEqual(1.08m, request.StopLoss);
		AreEqual(1.11m, request.TakeProfit);
		AreEqual("ORDER_TIME_GTC", request.Expiration.Type);
		IsNull(request.Expiration.Time);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionAndSynchronizationOptions()
	{
		var source = new MetaApiMessageAdapter(new IncrementalIdGenerator())
		{
			Token = "test-token".Secure(),
			AccountId = "account-1",
			Region = "london",
			Domain = "example.test",
			SynchronizationTimeout = TimeSpan.FromSeconds(45),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new MetaApiMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("test-token", target.Token.UnSecure());
		AreEqual(source.AccountId, target.AccountId);
		AreEqual(source.Region, target.Region);
		AreEqual(source.Domain, target.Domain);
		AreEqual(source.SynchronizationTimeout, target.SynchronizationTimeout);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(60000)]
	public async Task AuthenticatedApiSmoke()
	{
		var token = Environment.GetEnvironmentVariable("STOCKSHARP_METAAPI_TOKEN");
		var accountId = Environment.GetEnvironmentVariable(
			"STOCKSHARP_METAAPI_ACCOUNT");
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
				.EqualsIgnoreCase("1") || token.IsEmpty() || accountId.IsEmpty())
		{
			Inconclusive(
				"Set STOCKSHARP_LIVE_TESTS=1, STOCKSHARP_METAAPI_TOKEN, and " +
				"STOCKSHARP_METAAPI_ACCOUNT to run the MetaApi smoke test.");
		}

		var region = Environment.GetEnvironmentVariable("STOCKSHARP_METAAPI_REGION");
		var domain = Environment.GetEnvironmentVariable("STOCKSHARP_METAAPI_DOMAIN")
			.IsEmpty("agiliumtrade.agiliumtrade.ai");
		using var client = new MetaApiRestClient(domain, token.Secure(), region, 2);
		await client.GetServerSettingsAsync(CancellationToken);
		if (region.IsEmpty())
		{
			var account = await client.GetAccountAsync(accountId, CancellationToken);
			IsNotNull(account);
			region = account.Region;
		}
		IsFalse(region.IsEmpty());
		client.SetRegion(region);

		var information = await client.GetAccountInformationAsync(accountId,
			CancellationToken);
		IsNotNull(information);
		var symbols = await client.GetSymbolsAsync(accountId, CancellationToken);
		IsNotNull(symbols);
		IsGreater(symbols.Length, 0);
	}

	private static MetaApiSynchronizationPacket Packet(string type,
		string synchronizationId, long sequence)
		=> new()
		{
			Type = type,
			SynchronizationId = synchronizationId,
			SequenceNumber = sequence,
		};
}
