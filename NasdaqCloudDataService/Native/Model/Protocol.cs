namespace StockSharp.NasdaqCloudDataService.Native.Model;

/// <summary>Nasdaq equity data source.</summary>
public enum NasdaqCloudSources
{
	/// <summary>The Nasdaq Stock Market.</summary>
	[Display(Name = "Nasdaq")]
	Nasdaq,

	/// <summary>Nasdaq Texas.</summary>
	[Display(Name = "Nasdaq Texas")]
	Bx,

	/// <summary>Nasdaq PSX.</summary>
	[Display(Name = "Nasdaq PSX")]
	Psx,

	/// <summary>Consolidated quotes and trades.</summary>
	[Display(Name = "CQT")]
	Cqt,
}

/// <summary>Nasdaq Cloud market-data offset.</summary>
public enum NasdaqCloudOffsets
{
	/// <summary>Real-time data.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RealTimeKey)]
	Realtime,

	/// <summary>15-minute delayed data.</summary>
	[Display(Name = "Delayed")]
	Delayed,
}

enum NasdaqCloudBarRanges
{
	Day,
	FiveDays,
	Month,
	ThreeMonths,
	SixMonths,
	Year,
	FiveYears,
	Maximum,
	YearToDate,
}

sealed class NasdaqCloudAuthenticationRequest
{
	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("client_secret")]
	public string ClientSecret { get; set; }
}

sealed class NasdaqCloudAuthenticationResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }
}

sealed class NasdaqCloudErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	public string GetMessage()
		=> Message.IsEmpty(Detail).IsEmpty(Error).IsEmpty(Status);
}
