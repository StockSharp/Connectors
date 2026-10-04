namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.ETrade;
using StockSharp.Messages;

[TestClass]
public class ETradeTests : BaseTestClass
{
	[TestMethod]
	public async Task SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new ETradeMessageAdapter(new IncrementalIdGenerator())
		{
			Key = "public".Secure(),
			Secret = "secret".Secure(),
			Token = "token".Secure(),
			AccessSecret = "access".Secure(),
			IsDemo = true,
			RestEndpoint = "https://rest.example.test/",
			SandboxRestEndpoint = "https://sandbox.example.test/",
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);

		var target = new ETradeMessageAdapter(new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

		AreEqual(source.Id, target.Id);
		AreEqual("public", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual("token", target.Token.UnSecure());
		AreEqual("access", target.AccessSecret.UnSecure());
		IsTrue(target.IsDemo);
		AreEqual("https://rest.example.test/", target.RestEndpoint);
		AreEqual("https://sandbox.example.test/", target.SandboxRestEndpoint);
	}

	[TestMethod]
	public async Task ACopyArrivesWithTheSettingsItWasCopiedFrom()
	{
		var source = new ETradeMessageAdapter(new IncrementalIdGenerator())
		{
			Key = "public".Secure(),
			IsDemo = true,
		};

		var copy = (ETradeMessageAdapter)await source.CloneAsync(CancellationToken);

		AreEqual("public", copy.Key.UnSecure());
		IsTrue(copy.IsDemo);
	}
}
