namespace StockSharp.AlphaVantage;

using System;
using System.Collections.Generic;

using Ecng.Common;

using StockSharp.Messages;

static class Extensions
{
	public static IEnumerable<string> GetMonths(this DateTime from, DateTime to)
	{
		var current = new DateTime(from.Year, from.Month, 1, 0, 0, 0).UtcKind();
		var endDate = new DateTime(to.Year, to.Month, 1, 0, 0, 0).UtcKind();

		while (current <= endDate)
		{
			yield return current.ToString("yyyy-MM");

			current = current.AddMonths(1);
		}
	}

	public static SecurityTypes? ToSecurityType(this string type)
		=> type?.ToUpperInvariant() switch
		{
			"EQUITY" => SecurityTypes.Stock,
			"MUTUAL FUND" => SecurityTypes.Fund,
			"ETF" => SecurityTypes.Etf,
			_ => null,
		};
}