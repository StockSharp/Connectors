namespace StockSharp.Connectors.Tests;

using System;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Deriv;
using StockSharp.Deriv.Native;
using StockSharp.Deriv.Native.Model;
using StockSharp.Messages;

[TestClass]
public class DerivTests : BaseTestClass
{
	[TestMethod]
	public void ProtocolResponseParsesSubscriptionAndPayload()
	{
		var response = DerivResponse.Parse("""
			{"msg_type":"tick","req_id":42,"subscription":{"id":"sub-1"},"tick":{"symbol":"frxEURUSD","epoch":1700000000,"quote":1.08765,"bid":1.0876,"ask":1.0877,"pip_size":5}}
			""");

		AreEqual("tick", response.MessageType);
		AreEqual(42L, response.RequestId);
		AreEqual("sub-1", response.SubscriptionId);
		var tick = response.Get<DerivTick>("tick");
		AreEqual("frxEURUSD", tick.Symbol);
		AreEqual(1.08765m, tick.Quote);
		AreEqual(5, tick.PipSize);
	}

	[TestMethod]
	public void ProtocolErrorPreservesNativeDetails()
	{
		var response = DerivResponse.Parse("""
			{"msg_type":"proposal","req_id":7,"error":{"code":"ContractBuyValidationError","message":"Trading is not offered for this duration.","details":{"field":"duration"}}}
			""");

		var error = response.CreateException();
		IsTrue(error.Message.Contains("ContractBuyValidationError", StringComparison.Ordinal));
		IsTrue(error.Message.Contains("duration", StringComparison.Ordinal));
		IsTrue(error.Message.Contains("Trading is not offered", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ConditionStoresNativeContractParameters()
	{
		var condition = new DerivOrderCondition
		{
			ContractType = DerivContractTypes.Call,
			Basis = DerivBasisTypes.Stake,
			Currency = "USD",
			Duration = 15,
			DurationUnit = DerivDurationUnits.Minutes,
			Barrier = "+0.0005",
			Barrier2 = "-0.0005",
			Multiplier = 100,
			StopLoss = 3,
			TakeProfit = 5,
		};

		AreEqual(DerivContractTypes.Call, condition.ContractType);
		AreEqual(DerivBasisTypes.Stake, condition.Basis);
		AreEqual("USD", condition.Currency);
		AreEqual(15, condition.Duration);
		AreEqual(DerivDurationUnits.Minutes, condition.DurationUnit);
		AreEqual("+0.0005", condition.Barrier);
		AreEqual("-0.0005", condition.Barrier2);
		AreEqual(100m, condition.Multiplier);
		AreEqual(3m, condition.StopLoss);
		AreEqual(5m, condition.TakeProfit);
	}

	[TestMethod]
	public void ProposalRequestUsesOfficialWireValues()
	{
		var request = new DerivOrderCondition
		{
			ContractType = DerivContractTypes.Call,
			Basis = DerivBasisTypes.Stake,
			Currency = "USD",
			Duration = 15,
			DurationUnit = DerivDurationUnits.Minutes,
			Barrier = "+0.0005",
			StopLoss = 3,
			TakeProfit = 5,
		}.CreateProposalRequest("frxEURUSD", 25m);

		AreEqual(1, request.Value<int>("proposal"));
		AreEqual(25m, request.Value<decimal>("amount"));
		AreEqual("stake", request.Value<string>("basis"));
		AreEqual("CALL", request.Value<string>("contract_type"));
		AreEqual("USD", request.Value<string>("currency"));
		AreEqual("frxEURUSD", request.Value<string>("underlying_symbol"));
		AreEqual(15, request.Value<int>("duration"));
		AreEqual("m", request.Value<string>("duration_unit"));
		AreEqual("+0.0005", request.Value<string>("barrier"));
		AreEqual(3m, request["limit_order"].Value<decimal>("stop_loss"));
		AreEqual(5m, request["limit_order"].Value<decimal>("take_profit"));
	}

	[TestMethod]
	public void SettingsRoundTripKeepsAuthenticationAndEndpoints()
	{
		var source = new DerivMessageAdapter(new IncrementalIdGenerator())
		{
			Token = "secret-token".Secure(),
			AppId = "12345",
			AccountId = "DOT90004580",
			IsDemo = false,
			RestAddress = new("https://example.test"),
			PublicWebSocketAddress = "wss://example.test/public",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new DerivMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("secret-token", target.Token.UnSecure());
		AreEqual(source.AppId, target.AppId);
		AreEqual(source.AccountId, target.AccountId);
		AreEqual(source.IsDemo, target.IsDemo);
		AreEqual(source.RestAddress, target.RestAddress);
		AreEqual(source.PublicWebSocketAddress, target.PublicWebSocketAddress);
	}

	[TestMethod]
	public void CandleGranularityUsesOfficialIntervals()
	{
		AreEqual(60, TimeSpan.FromMinutes(1).ToDerivGranularity());
		AreEqual(86400, TimeSpan.FromDays(1).ToDerivGranularity());
		ThrowsExactly<ArgumentOutOfRangeException>(() =>
			TimeSpan.FromMinutes(7).ToDerivGranularity());
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(30000)]
	public async Task PublicApiSmoke()
	{
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 to run Deriv public API smoke tests.");

		using var client = new DerivWebSocketClient(
			static _ => new("wss://api.derivws.com/trading/v1/options/ws/public"), 0);
		await client.ConnectAsync(CancellationToken);
		try
		{
			var symbolsResponse = await client.RequestAsync(new()
			{
				["active_symbols"] = "brief",
			}, CancellationToken);
			var symbols = symbolsResponse.GetArray<DerivActiveSymbol>("active_symbols");
			IsGreater(symbols.Length, 0);
			var symbol = symbols[0].Symbol;

			var tickResponse = await client.SubscribeAsync("smoke-tick", new()
			{
				["ticks"] = symbol,
				["subscribe"] = 1,
			}, true, CancellationToken);
			var tick = tickResponse.Get<DerivTick>("tick");
			IsNotNull(tick);
			AreEqual(symbol, tick.Symbol);
			IsGreater(tick.Epoch, 0L);
			await client.UnsubscribeAsync("smoke-tick", CancellationToken);
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
		}
	}
}
