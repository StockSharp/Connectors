namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Reflection;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
sealed class RegionalLiveTestsAttribute : Attribute
{
	private readonly string _testName;

	public RegionalLiveTestsAttribute()
	{
	}

	public RegionalLiveTestsAttribute(string testName) => _testName = testName.ThrowIfEmpty(nameof(testName));

	public bool AppliesTo(string testName) => _testName is null || _testName == testName;
}

static class LiveTestAccess
{
	public const string RegionalOptInVariable = "STOCKSHARP_LIVE_REGIONAL_TESTS";
	public const string MoexDividendOptInVariable = "STOCKSHARP_LIVE_MOEX_DIVIDENDS_TESTS";

	/// <summary>
	/// Returns the reason to skip a live check for the supplied opt-in value.
	/// </summary>
	public static string GetLiveSkipReason(string setting)
		=> setting.EqualsIgnoreCase("1") ? null : "Set STOCKSHARP_LIVE_TESTS=1 for live API tests.";

	/// <summary>
	/// Applies live access requirements only to integration methods in a mixed test class.
	/// </summary>
	public static string GetMarketDataSkipReason(Type fixtureType, string testName, string liveSetting, string regionalSetting)
	{
		if (testName.IsEmpty() || fixtureType.GetMethod(testName)?.GetCustomAttributes<TestCategoryAttribute>(true)
			.Any(category => category.TestCategories.Contains("Integration")) != true)
			return null;

		return GetLiveSkipReason(liveSetting) ?? GetRegionalSkipReason(fixtureType, testName, regionalSetting);
	}

	/// <summary>
	/// Requires explicit dividend access without restricting public ISS history.
	/// </summary>
	public static string GetMoexDividendSkipReason(string testName, string liveSetting, string dividendSetting)
	{
		if (testName != nameof(MoexISSTests.MoexIssDivSber))
			return null;

		return GetLiveSkipReason(liveSetting) ?? (dividendSetting.EqualsIgnoreCase("1")
			? null
			: $"Set {MoexDividendOptInVariable}=1 to run MOEX dividend-history checks on an ISS service providing dividend data.");
	}

	public static string GetRegionalSkipReason(Type fixtureType, string testName, string setting)
	{
		if (setting.EqualsIgnoreCase("1"))
			return null;

		var isRestricted = fixtureType.GetCustomAttributes<RegionalLiveTestsAttribute>(true)
			.Any(restriction => restriction.AppliesTo(testName));
		if (!isRestricted && !testName.IsEmpty())
			isRestricted = fixtureType.GetMethod(testName)?.GetCustomAttributes<RegionalLiveTestsAttribute>(true)
				.Any(restriction => restriction.AppliesTo(testName)) == true;
		return isRestricted
			? $"Set {RegionalOptInVariable}=1 to run venue tests restricted by the runner's network or region."
			: null;
	}
}
