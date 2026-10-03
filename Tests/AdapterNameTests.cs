namespace StockSharp.Connectors.Tests;

using System.Collections.Generic;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Alpaca;
using StockSharp.ByBit;
using StockSharp.LMAX;
using StockSharp.Messages;

/// <summary>
/// How a connection names itself where its settings are listed: by its name, and by its key once it has one.
/// </summary>
/// <remarks>
/// A connection without a key yet was named "ByBit: Ключ =", a sentence that stops halfway, at the top of the
/// settings of every new ByBit source.
/// </remarks>
[TestClass]
public class AdapterNameTests : BaseTestClass
{
	private static IEnumerable<MessageAdapter> Adapters()
	{
		yield return new AlpacaMessageAdapter(new IncrementalIdGenerator());
		yield return new ByBitMessageAdapter(new IncrementalIdGenerator());
		yield return new LmaxMessageAdapter(new IncrementalIdGenerator());
	}

	[TestMethod]
	public void AConnectionWithoutAKeyIsNamedWithoutOne()
	{
		foreach (var adapter in Adapters())
		{
			var name = adapter.ToString();

			IsFalse(name.Contains('='), name);
			IsFalse(name.TrimEnd().EndsWith(':'), name);
		}
	}

	[TestMethod]
	public void AConnectionWithAKeyNamesIt()
	{
		foreach (var adapter in Adapters())
		{
			var key = "KEY123456".Secure();
			((IKeySecretAdapter)adapter).Key = key;

			IsTrue(adapter.ToString().Contains($"{key.ToId()}"), adapter.ToString());
		}
	}
}
