namespace StockSharp.InteractiveBrokers;

/// <summary>
/// The filter result of scanner starting via <see cref="ScannerMarketDataMessage"/>.
/// </summary>
public class ScannerResult : IPersistable
{
	/// <summary>
	/// Security ID.
	/// </summary>
	public SecurityId SecurityId { get; set; }
	
	/// <summary>
	/// Rank.
	/// </summary>
	public int Rank { get; set; }

	/// <summary>
	/// Distance.
	/// </summary>
	public string Distance { get; set; }

	/// <summary>
	/// Benchmark.
	/// </summary>
	public string Benchmark { get; set; }

	/// <summary>
	/// Projection.
	/// </summary>
	public string Projection { get; set; }

	/// <summary>
	/// The combined instrument description.
	/// </summary>
	public string Legs { get; set; }

	void IPersistable.Load(SettingsStorage storage)
	{
		if (storage.ContainsKey(nameof(SecurityId)))
			SecurityId = storage.GetValue<string>(nameof(SecurityId)).ToSecurityId();

		Rank = storage.GetValue<int>(nameof(Rank));
		Distance = storage.GetValue<string>(nameof(Distance));
		Benchmark = storage.GetValue<string>(nameof(Benchmark));
		Projection = storage.GetValue<string>(nameof(Projection));
		Legs = storage.GetValue<string>(nameof(Legs));
	}

	void IPersistable.Save(SettingsStorage storage)
	{
		if (SecurityId != default)
			storage.SetValue(nameof(SecurityId), SecurityId.ToStringId());

		storage.SetValue(nameof(Rank), Rank);
		storage.SetValue(nameof(Distance), Distance);
		storage.SetValue(nameof(Benchmark), Benchmark);
		storage.SetValue(nameof(Projection), Projection);
		storage.SetValue(nameof(Legs), Legs);
	}
}