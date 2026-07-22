namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.StocksTrader;
using StockSharp.StocksTrader.Native;
using StockSharp.StocksTrader.Native.Model;

[TestClass]
public class StocksTraderTests : BaseTestClass
{
	[TestMethod]
	public void ProtocolParsesMarginAccountState()
	{
		var state = StocksTraderProtocol.Parse<StocksTraderAccountState>("""
			{"code":"ok","data":{"margin":{"balance":10000.5,"unrealized_pl":-12.3,"equity":9988.2,"margin":250.1,"free_margin":9738.1}}}
			""");

		IsNotNull(state.Margin);
		AreEqual(10000.5m, state.Margin.Balance);
		AreEqual(-12.3m, state.Margin.UnrealizedPnL);
		AreEqual(9988.2m, state.Margin.Equity);
		AreEqual(250.1m, state.Margin.Margin);
		AreEqual(9738.1m, state.Margin.FreeMargin);
	}

	[TestMethod]
	public void ProtocolPreservesApiError()
	{
		var error = ThrowsExactly<InvalidOperationException>(() =>
			StocksTraderProtocol.Parse<StocksTraderAccount[]>("""
				{"code":"error","msg":"The account is unavailable."}
				"""));

		IsTrue(error.Message.Contains("The account is unavailable", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ProtocolParsesOrderAndDealLinks()
	{
		var orders = StocksTraderProtocol.Parse<StocksTraderOrder[]>("""
			{"code":"ok","data":[{"id":"002212096","ticker":"EURUSD","volume":2,"side":"buy","type":"limit","filled_price":1.085,"price":1.084,"expiration":1705888778,"last_modified":1704879978,"create_time":1704276610,"deals":["001389"],"status":"filled"}]}
			""");

		AreEqual(1, orders.Length);
		AreEqual("002212096", orders[0].Id);
		AreEqual("001389", orders[0].Deals.Single());
		AreEqual(OrderStates.Done, orders[0].Status.ToOrderState());
		AreEqual(OrderTypes.Limit, orders[0].Type.ToOrderType());
	}

	[TestMethod]
	public void PlaceOrderUsesOfficialFormFields()
	{
		var form = new StocksTraderOrderRequest
		{
			Ticker = "EURUSD",
			Volume = 2.5m,
			Side = "buy",
			Type = "limit",
			Price = 1.0845m,
			Expiration = 1706528750,
			StopLoss = 1.08m,
			TakeProfit = 1.09m,
		}.ToForm();

		AreEqual("EURUSD", form.Single(pair => pair.Key == "ticker").Value);
		AreEqual("2.5", form.Single(pair => pair.Key == "volume").Value);
		AreEqual("buy", form.Single(pair => pair.Key == "side").Value);
		AreEqual("limit", form.Single(pair => pair.Key == "type").Value);
		AreEqual("1.0845", form.Single(pair => pair.Key == "price").Value);
		AreEqual("1706528750", form.Single(pair => pair.Key == "expiration").Value);
		AreEqual("1.08", form.Single(pair => pair.Key == "stop_loss").Value);
		AreEqual("1.09", form.Single(pair => pair.Key == "take_profit").Value);
	}

	[TestMethod]
	public void InstrumentMapsTradingRulesAndType()
	{
		var forex = new StocksTraderInstrument
		{
			Ticker = "EURUSD",
			Description = "EUR/USD",
			ContractSize = 1,
			Units = "Currency",
			MinVolume = 1000,
			MaxVolume = 100000000,
			VolumeStep = 1000,
			MinTick = 0.00001m,
		};
		var security = forex.ToSecurityMessage(42);

		AreEqual(SecurityTypes.Currency, security.SecurityType);
		AreEqual("EURUSD", security.SecurityId.SecurityCode);
		AreEqual(StocksTraderExtensions.BoardCode, security.SecurityId.BoardCode);
		AreEqual(0.00001m, security.PriceStep);
		AreEqual(1000m, security.VolumeStep);
		AreEqual(1000m, security.MinVolume);
		AreEqual(100000000m, security.MaxVolume);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsTokenAccountAndPolling()
	{
		var source = new StocksTraderMessageAdapter(new IncrementalIdGenerator())
		{
			Token = "test-token".Secure(),
			AccountId = "93012898",
			IsDemo = false,
			Address = new("https://example.test/"),
			PollingInterval = TimeSpan.FromSeconds(7),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new StocksTraderMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("test-token", target.Token.UnSecure());
		AreEqual(source.AccountId, target.AccountId);
		AreEqual(source.IsDemo, target.IsDemo);
		AreEqual(source.Address, target.Address);
		AreEqual(source.PollingInterval, target.PollingInterval);
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(30000)]
	public async Task AuthenticatedApiSmoke()
	{
		var token = Environment.GetEnvironmentVariable(
			"STOCKSHARP_STOCKSTRADER_TOKEN");
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
				.EqualsIgnoreCase("1") || token.IsEmpty())
		{
			Inconclusive(
				"Set STOCKSHARP_LIVE_TESTS=1 and STOCKSHARP_STOCKSTRADER_TOKEN " +
				"to run the StocksTrader API smoke test.");
		}

		using var client = new StocksTraderClient(
			new("https://api.stockstrader.com/"), token.Secure(), 2);
		var accounts = await client.GetAccountsAsync(CancellationToken);
		IsGreater(accounts.Length, 0);
		var account = accounts.FirstOrDefault(item =>
			!item.Status.EqualsIgnoreCase("disabled"));
		IsNotNull(account);

		var instruments = await client.GetInstrumentsAsync(account.Id,
			CancellationToken);
		IsGreater(instruments.Length, 0);
	}
}
