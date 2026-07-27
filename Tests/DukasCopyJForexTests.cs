namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DukasCopyJForex;
using StockSharp.Messages;

[TestClass]
public class DukasCopyJForexTests : BaseTestClass
{
	[TestMethod]
	public void SettingsRoundTripKeepsJForexConnectionOptions()
	{
		var source = new DukasCopyJForexMessageAdapter(
			new IncrementalIdGenerator())
		{
			Login = "demo-user",
			Password = "demo-password".Secure(),
			IsDemo = false,
			DemoAddress = new("https://demo.example.test/jforex.jnlp"),
			LiveAddress = new("https://live.example.test/jforex.jnlp"),
			BridgePort = 31415,
			BridgeJarPath = @"C:\bridges\dukascopy-jforex.jar",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new DukasCopyJForexMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.Login, target.Login);
		AreEqual("demo-password", target.Password.UnSecure());
		AreEqual(source.IsDemo, target.IsDemo);
		AreEqual(source.DemoAddress, target.DemoAddress);
		AreEqual(source.LiveAddress, target.LiveAddress);
		AreEqual(source.BridgePort, target.BridgePort);
		AreEqual(source.BridgeJarPath, target.BridgeJarPath);
	}

	[TestMethod]
	public void DefaultsUseOfficialJForexServiceAddresses()
	{
		var adapter = new DukasCopyJForexMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://platform.dukascopy.com/demo_3/jforex_3.jnlp",
			adapter.DemoAddress.AbsoluteUri);
		AreEqual("https://platform.dukascopy.com/live_3/jforex_3.jnlp",
			adapter.LiveAddress.AbsoluteUri);
	}

	[TestMethod]
	public void OrderConditionKeepsJForexParameters()
	{
		var condition = new DukasCopyJForexOrderCondition
		{
			NativeCommand = DukasCopyJForexOrderCommands.SellStopByAsk,
			Slippage = 2.5m,
			StopLoss = 1.05m,
			TakeProfit = 1.15m,
			Comment = "stocksharp",
		};

		AreEqual(DukasCopyJForexOrderCommands.SellStopByAsk,
			condition.NativeCommand);
		AreEqual(2.5m, condition.Slippage);
		AreEqual(1.05m, condition.StopLoss);
		AreEqual(1.15m, condition.TakeProfit);
		AreEqual("stocksharp", condition.Comment);
	}
}
