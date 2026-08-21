namespace StockSharp.Connectors.Tests;

using System;
using System.Globalization;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Fix.Quik.Lua;
using StockSharp.Messages;

/// <summary>
/// Verifies the opaque QUIK order-condition payload used by transports without venue-specific fields.
/// </summary>
[TestClass]
public class QuikOrderConditionWireTests : BaseTestClass
{
	private static readonly DateTime _activeFrom = new DateTime(2026, 8, 20, 9, 30, 15, DateTimeKind.Utc).AddTicks(1_234_560);
	private static readonly DateTime _activeTo = new DateTime(2026, 8, 20, 18, 45, 30, DateTimeKind.Utc).AddTicks(6_543_210);
	private static readonly DateTime _repoSettleDate = new DateTime(2026, 8, 21, 10, 15, 0, DateTimeKind.Utc).AddTicks(1_111_110);
	private static readonly DateTime _ntmSettleDate = new DateTime(2026, 8, 22, 11, 25, 0, DateTimeKind.Utc).AddTicks(2_222_220);

	/// <summary>
	/// Every business property, including both nested blocks, survives one version-one round trip.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void AllBusinessFields_RoundTrip()
	{
		var source = new QuikOrderCondition
		{
			Type = QuikOrderConditionTypes.TakeProfitStopLimit,
			OtherSecurityId = new SecurityId { SecurityCode = "GAZP", BoardCode = "TQBR" },
			StopPriceCondition = QuikStopPriceConditions.LessOrEqual,
			StopPrice = 249.5m,
			StopLimitPrice = 248.25m,
			IsMarketStopLimit = true,
			ActiveTime = (_activeFrom, _activeTo),
			ConditionOrderId = 9_876_543_210L,
			ConditionOrderSide = Sides.Sell,
			ConditionOrderPartiallyMatched = true,
			ConditionOrderUseMatchedBalance = false,
			LinkedOrderPrice = 247.75m,
			LinkedOrderCancel = false,
			Offset = new Unit(3m, UnitTypes.Percent),
			Spread = new Unit(0.5m, UnitTypes.Absolute),
			IsMarketTakeProfit = false,
			IsRepo = true,
			RepoInfo = new RepoOrderInfo
			{
				Partner = "REPO-PARTNER",
				Term = 7.5m,
				Rate = 12.25m,
				BlockSecurities = true,
				RefundRate = 1.75m,
				MatchRef = "REPO-REF",
				SettleCode = "T1",
				SecondPrice = 245.125m,
				SettleDate = _repoSettleDate,
				StartDiscount = 10.1m,
				LowerDiscount = 5.2m,
				UpperDiscount = 15.3m,
				Value = 1_000_000.45m,
				IsModified = true,
			},
			IsNtm = true,
			NtmInfo = new NtmOrderInfo
			{
				Partner = "NTM-PARTNER",
				SettleDate = _ntmSettleDate,
				MatchRef = "NTM-REF",
				SettleCode = "T2",
				ForAccount = "CLIENT-ACCOUNT",
				CurrencyType = CurrencyTypes.RUB,
			},
		};

		var payload = source.ToWire();

		IsTrue(payload.StartsWith("v1;", StringComparison.Ordinal));
		IsTrue(payload.Contains($"Type={(int)source.Type.Value}", StringComparison.Ordinal));
		IsTrue(payload.Contains($"StopPriceCondition={(int)source.StopPriceCondition.Value}", StringComparison.Ordinal));
		IsTrue(payload.Contains($"ConditionOrderSide={(int)source.ConditionOrderSide.Value}", StringComparison.Ordinal));
		IsTrue(payload.Contains($"Ntm.CurrencyType={(int)source.NtmInfo.CurrencyType}", StringComparison.Ordinal));
		IsFalse(payload.Contains(nameof(QuikOrderConditionTypes.TakeProfitStopLimit), StringComparison.Ordinal));
		IsTrue(payload.Contains($"ActiveTime.From={_activeFrom.ToUnixMcs()}", StringComparison.Ordinal));
		IsTrue(payload.Contains($"ActiveTime.To={_activeTo.ToUnixMcs()}", StringComparison.Ordinal));
		IsTrue(payload.Contains($"Repo.SettleDate={_repoSettleDate.ToUnixMcs()}", StringComparison.Ordinal));
		IsTrue(payload.Contains($"Ntm.SettleDate={_ntmSettleDate.ToUnixMcs()}", StringComparison.Ordinal));
		IsTrue(payload.Contains("OtherSecurityId=GAZP@TQBR", StringComparison.Ordinal));
		IsTrue(payload.Contains("Offset=3%25", StringComparison.Ordinal));
		IsTrue(payload.Contains("Spread=0.5", StringComparison.Ordinal));

		var restored = new QuikOrderCondition();
		restored.FromWire(payload);

		AreEqual(source.Type, restored.Type);
		AreEqual(source.OtherSecurityId, restored.OtherSecurityId);
		AreEqual(source.StopPriceCondition, restored.StopPriceCondition);
		AreEqual(source.StopPrice, restored.StopPrice);
		AreEqual(source.StopLimitPrice, restored.StopLimitPrice);
		AreEqual(source.IsMarketStopLimit, restored.IsMarketStopLimit);
		AreEqual(source.ActiveTime.from, restored.ActiveTime.from);
		AreEqual(source.ActiveTime.to, restored.ActiveTime.to);
		AreEqual(source.ConditionOrderId, restored.ConditionOrderId);
		AreEqual(source.ConditionOrderSide, restored.ConditionOrderSide);
		AreEqual(source.ConditionOrderPartiallyMatched, restored.ConditionOrderPartiallyMatched);
		AreEqual(source.ConditionOrderUseMatchedBalance, restored.ConditionOrderUseMatchedBalance);
		AreEqual(source.LinkedOrderPrice, restored.LinkedOrderPrice);
		AreEqual(source.LinkedOrderCancel, restored.LinkedOrderCancel);
		AreEqual(source.Offset, restored.Offset);
		AreEqual(source.Spread, restored.Spread);
		AreEqual(source.IsMarketTakeProfit, restored.IsMarketTakeProfit);
		AreEqual(source.IsRepo, restored.IsRepo);
		AreEqual(source.RepoInfo.Partner, restored.RepoInfo.Partner);
		AreEqual(source.RepoInfo.Term, restored.RepoInfo.Term);
		AreEqual(source.RepoInfo.Rate, restored.RepoInfo.Rate);
		AreEqual(source.RepoInfo.BlockSecurities, restored.RepoInfo.BlockSecurities);
		AreEqual(source.RepoInfo.RefundRate, restored.RepoInfo.RefundRate);
		AreEqual(source.RepoInfo.MatchRef, restored.RepoInfo.MatchRef);
		AreEqual(source.RepoInfo.SettleCode, restored.RepoInfo.SettleCode);
		AreEqual(source.RepoInfo.SecondPrice, restored.RepoInfo.SecondPrice);
		AreEqual(source.RepoInfo.SettleDate, restored.RepoInfo.SettleDate);
		AreEqual(source.RepoInfo.StartDiscount, restored.RepoInfo.StartDiscount);
		AreEqual(source.RepoInfo.LowerDiscount, restored.RepoInfo.LowerDiscount);
		AreEqual(source.RepoInfo.UpperDiscount, restored.RepoInfo.UpperDiscount);
		AreEqual(source.RepoInfo.Value, restored.RepoInfo.Value);
		AreEqual(source.RepoInfo.IsModified, restored.RepoInfo.IsModified);
		AreEqual(source.IsNtm, restored.IsNtm);
		AreEqual(source.NtmInfo.Partner, restored.NtmInfo.Partner);
		AreEqual(source.NtmInfo.SettleDate, restored.NtmInfo.SettleDate);
		AreEqual(source.NtmInfo.MatchRef, restored.NtmInfo.MatchRef);
		AreEqual(source.NtmInfo.SettleCode, restored.NtmInfo.SettleCode);
		AreEqual(source.NtmInfo.ForAccount, restored.NtmInfo.ForAccount);
		AreEqual(source.NtmInfo.CurrencyType, restored.NtmInfo.CurrencyType);
	}

	/// <summary>
	/// Cloning is statically bound to the QUIK implementation and keeps mutable nested blocks independent.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void Clone_IsAotSafeAndDeepCopiesParameters()
	{
		var source = new QuikOrderCondition
		{
			Type = QuikOrderConditionTypes.StopLimit,
			StopPrice = 249.5m,
			Offset = new Unit(3m, UnitTypes.Percent),
			Spread = new Unit(0.5m, UnitTypes.Absolute),
			RepoInfo = new RepoOrderInfo { Partner = "REPO" },
			NtmInfo = new NtmOrderInfo { Partner = "NTM" },
		};

		QuikOrderCondition clone = source.Clone();

		AreNotSame(source, clone);
		AreNotSame(source.Offset, clone.Offset);
		AreNotSame(source.Spread, clone.Spread);
		AreNotSame(source.RepoInfo, clone.RepoInfo);
		AreNotSame(source.NtmInfo, clone.NtmInfo);
		AreEqual(source.Type, clone.Type);
		AreEqual(source.StopPrice, clone.StopPrice);
		AreEqual(source.Offset, clone.Offset);
		AreEqual(source.Spread, clone.Spread);
		AreEqual(source.RepoInfo.Partner, clone.RepoInfo.Partner);
		AreEqual(source.NtmInfo.Partner, clone.NtmInfo.Partner);

		source.Offset.Value = 4m;
		source.Spread.Value = 1m;
		source.RepoInfo.Partner = "CHANGED";
		source.NtmInfo.Partner = "CHANGED";

		AreEqual(3m, clone.Offset.Value);
		AreEqual(0.5m, clone.Spread.Value);
		AreEqual("REPO", clone.RepoInfo.Partner);
		AreEqual("NTM", clone.NtmInfo.Partner);
	}

	/// <summary>
	/// Future cloneable parameters retain the base condition's deep-copy contract without type-specific code.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void Clone_DeepCopiesUnknownCloneableParameter()
	{
		var value = new FutureCloneableParameter { Value = 42 };
		var source = new QuikOrderCondition();
		source.Parameters["Future"] = value;

		var clone = source.Clone();
		var clonedValue = (FutureCloneableParameter)clone.Parameters["Future"];

		AreNotSame(value, clonedValue);
		AreEqual(value.Value, clonedValue.Value);

		value.Value = 43;

		AreEqual(42, clonedValue.Value);
	}

	/// <summary>
	/// Null fields cost no payload space, while an explicitly empty string remains an explicit value.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void NullAndEmptyFields_RoundTrip()
	{
		var source = new QuikOrderCondition();
		source.RepoInfo.Partner = string.Empty;
		source.NtmInfo.ForAccount = string.Empty;

		var payload = source.ToWire();

		IsFalse(payload.Contains(";Type=", StringComparison.Ordinal));
		IsFalse(payload.Contains(";OtherSecurityId=", StringComparison.Ordinal));
		IsFalse(payload.Contains(";StopPrice=", StringComparison.Ordinal));
		IsFalse(payload.Contains(";Offset=", StringComparison.Ordinal));
		IsFalse(payload.Contains(";Repo.MatchRef=", StringComparison.Ordinal));
		IsTrue(payload.Contains("Repo.Partner=", StringComparison.Ordinal));
		IsTrue(payload.Contains("Ntm.ForAccount=", StringComparison.Ordinal));

		var restored = new QuikOrderCondition();
		restored.FromWire(payload);

		IsNull(restored.Type);
		IsNull(restored.OtherSecurityId);
		IsNull(restored.StopPrice);
		IsNull(restored.Offset);
		AreEqual(default((DateTime from, DateTime to)), restored.ActiveTime);
		AreEqual(string.Empty, restored.RepoInfo.Partner);
		AreEqual(string.Empty, restored.NtmInfo.ForAccount);

		new QuikOrderCondition().FromWire(null);
		new QuikOrderCondition().FromWire(string.Empty);
	}

	/// <summary>
	/// The three reserved value characters are escaped once and restored without recursive decoding.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void ReservedCharacters_RoundTrip()
	{
		const string partner = "literal%3B;left=right%";
		const string account = "ACC%=ONE;TWO";

		var source = new QuikOrderCondition();
		source.RepoInfo.Partner = partner;
		source.NtmInfo.ForAccount = account;

		var payload = source.ToWire();

		IsTrue(payload.Contains("Repo.Partner=literal%253B%3Bleft%3Dright%25", StringComparison.Ordinal));
		IsTrue(payload.Contains("Ntm.ForAccount=ACC%25%3DONE%3BTWO", StringComparison.Ordinal));

		var restored = new QuikOrderCondition();
		restored.FromWire(payload);

		AreEqual(partner, restored.RepoInfo.Partner);
		AreEqual(account, restored.NtmInfo.ForAccount);
	}

	/// <summary>
	/// Fields added by a newer writer do not prevent an older version-one reader from using known data.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void UnknownKeys_AreIgnored()
	{
		var condition = new QuikOrderCondition();

		condition.FromWire("v1;Future.Value=ignored%3Bvalue%3D1;StopPrice=123.45");

		AreEqual(123.45m, condition.StopPrice);
	}

	/// <summary>
	/// A payload stamped with a version this reader does not know is refused outright. Reading it as far as
	/// it happens to parse would hand the venue a condition assembled from a format nobody agreed on.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void ForeignVersion_IsRefused()
	{
		Throws<ArgumentOutOfRangeException>(() => new QuikOrderCondition().FromWire("v2;StopPrice=123.45"));
		Throws<ArgumentOutOfRangeException>(() => new QuikOrderCondition().FromWire("V1;StopPrice=123.45"));
		Throws<ArgumentOutOfRangeException>(() => new QuikOrderCondition().FromWire("StopPrice=123.45"));
	}

	/// <summary>
	/// The two ends of the activity window travel as separate keys, so a window open at one end keeps that
	/// end and leaves the other at its default.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void HalfOpenActiveTime_RoundTrips()
	{
		var fromPayload = new QuikOrderCondition { ActiveTime = (_activeFrom, default) }.ToWire();

		IsTrue(fromPayload.Contains($"ActiveTime.From={_activeFrom.ToUnixMcs()}", StringComparison.Ordinal), fromPayload);
		IsFalse(fromPayload.Contains("ActiveTime.To=", StringComparison.Ordinal), fromPayload);

		var openEnd = new QuikOrderCondition();
		openEnd.FromWire(fromPayload);

		AreEqual(_activeFrom, openEnd.ActiveTime.from);
		AreEqual(default(DateTime), openEnd.ActiveTime.to);

		var toPayload = new QuikOrderCondition { ActiveTime = (default, _activeTo) }.ToWire();

		IsFalse(toPayload.Contains("ActiveTime.From=", StringComparison.Ordinal), toPayload);
		IsTrue(toPayload.Contains($"ActiveTime.To={_activeTo.ToUnixMcs()}", StringComparison.Ordinal), toPayload);

		var openStart = new QuikOrderCondition();
		openStart.FromWire(toPayload);

		AreEqual(default(DateTime), openStart.ActiveTime.from);
		AreEqual(_activeTo, openStart.ActiveTime.to);
	}

	/// <summary>
	/// Serialization is performed under the invariant culture, which is the form the reader parses with:
	/// numbers keep a dot, and a Unit keeps its own suffix after the escaped percent sign.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void Serialization_RunsUnderInvariantCulture()
	{
		var source = new QuikOrderCondition
		{
			StopPrice = 249.5m,
			LinkedOrderPrice = -0.75m,
			Offset = new Unit(3.5m, UnitTypes.Percent),
			Spread = new Unit(0.25m, UnitTypes.Absolute),
		};

		var restore = CultureInfo.CurrentCulture;
		string payload;
		QuikOrderCondition restored;

		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

		try
		{
			payload = source.ToWire();

			restored = new QuikOrderCondition();
			restored.FromWire(payload);
		}
		finally
		{
			CultureInfo.CurrentCulture = restore;
		}

		IsTrue(payload.Contains("StopPrice=249.5", StringComparison.Ordinal), payload);
		IsTrue(payload.Contains("LinkedOrderPrice=-0.75", StringComparison.Ordinal), payload);
		IsTrue(payload.Contains("Offset=3.5%25", StringComparison.Ordinal), payload);
		IsTrue(payload.Contains("Spread=0.25", StringComparison.Ordinal), payload);

		AreEqual(source.StopPrice, restored.StopPrice);
		AreEqual(source.LinkedOrderPrice, restored.LinkedOrderPrice);
		AreEqual(source.Offset, restored.Offset);
		AreEqual(source.Spread, restored.Spread);
	}

	/// <summary>
	/// A pair that carries no value is stepped over, so one damaged field cannot cost the rest of the
	/// payload.
	/// </summary>
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public void FieldsWithoutAValue_AreSteppedOver()
	{
		var condition = new QuikOrderCondition();

		condition.FromWire("v1;;StopPriceCondition;=123;StopPrice=123.45");

		AreEqual(123.45m, condition.StopPrice);
		IsNull(condition.StopPriceCondition);
	}

	private sealed class FutureCloneableParameter : ICloneable
	{
		public int Value { get; set; }

		public object Clone()
			=> new FutureCloneableParameter { Value = Value };
	}
}
