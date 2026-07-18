namespace StockSharp.TwelveData.Native.Model;

class TwelveDataResponse
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	public bool IsError => Status.EqualsIgnoreCase("error") || Code is >= 400;
}

sealed class TwelveDataApiUsage : TwelveDataResponse
{
	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("current_usage")]
	public long? CurrentUsage { get; set; }

	[JsonProperty("plan_limit")]
	public long? PlanLimit { get; set; }

	[JsonProperty("daily_usage")]
	public long? DailyUsage { get; set; }

	[JsonProperty("plan_daily_limit")]
	public long? PlanDailyLimit { get; set; }

	[JsonProperty("plan_category")]
	public string PlanCategory { get; set; }
}
