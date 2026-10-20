namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Reflection;

using Ecng.Common;

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
			? $"Set {RegionalOptInVariable}=1 to run region-restricted venue tests on a runner permitted to access the venue."
			: null;
	}
}
