namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Fix;
using StockSharp.Fix.Native;
using StockSharp.Fix.Quik.Lua;
using StockSharp.Messages;

/// <summary>
/// Verifies the private tags a QUIK order condition travels through on the text wire.
/// </summary>
[TestClass]
public class QuikOrderConditionFixTests : BaseTestClass
{
	private static readonly FastDateTimeParser _dateTimeParser = new("yyyyMMdd-HH:mm:ss");

	/// <summary>
	/// The board is what the terminal resolves an instrument's class code from, so a condition that
	/// names another instrument has to carry both halves of its identifier. Carrying the code alone
	/// leaves the reader with an id no class matches, and the transaction cannot be built at all.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task OtherSecurityKeepsItsBoard()
	{
		var source = new QuikOrderCondition
		{
			Type = QuikOrderConditionTypes.OtherSecurity,
			OtherSecurityId = new SecurityId { SecurityCode = "SBER", BoardCode = "QJSIM" },
			StopPriceCondition = QuikStopPriceConditions.MoreOrEqual,
			StopPrice = 300.5m,
		};

		var restored = await RoundTripAsync(source);

		IsNotNull(restored.OtherSecurityId);
		AreEqual("SBER", restored.OtherSecurityId.Value.SecurityCode);
		AreEqual("QJSIM", restored.OtherSecurityId.Value.BoardCode);
		AreEqual(source.Type, restored.Type);
		AreEqual(source.StopPriceCondition, restored.StopPriceCondition);
		AreEqual(source.StopPrice, restored.StopPrice);
	}

	/// <summary>
	/// A condition that names no other instrument writes neither half, and reads back without one.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task NoOtherSecurityStaysAbsent()
	{
		var restored = await RoundTripAsync(new QuikOrderCondition
		{
			Type = QuikOrderConditionTypes.StopLimit,
			StopPrice = 249.5m,
		});

		IsNull(restored.OtherSecurityId);
		AreEqual(249.5m, restored.StopPrice);
	}

	private static async Task<QuikOrderCondition> RoundTripAsync(QuikOrderCondition source)
	{
		using var stream = new MemoryStream();

		IFixWriter writer = new TextFixWriter(stream, Encoding.ASCII);
		await writer.WriteOrderConditionAsync(source, _dateTimeParser, CancellationToken.None);
		await writer.FlushAsync(CancellationToken.None);

		stream.Position = 0;

		IFixReader reader = new TextFixReader(stream, Encoding.ASCII);
		QuikOrderCondition restored = null;

		try
		{
			while (stream.Position < stream.Length)
			{
				var tag = await reader.ReadTagAsync(CancellationToken.None);

				await reader.ReadOrderConditionAsync(tag, _dateTimeParser,
					() => restored ??= new QuikOrderCondition(), CancellationToken.None);
			}
		}
		catch (EndOfStreamException)
		{
		}

		return restored ?? new QuikOrderCondition();
	}
}
