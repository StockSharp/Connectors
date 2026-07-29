namespace StockSharp.Transaq;

/// <summary>
/// Уровни логирования.
/// </summary>
public enum ApiLogLevels
{
	/// <summary>
	/// Минимально.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MinimumKey)]
	Min = 1,

	/// <summary>
	/// Стандарт (оптимально).
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StandardKey)]
	Standard = 2,

	/// <summary>
	/// Максимально.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MaximumKey)]
	Max = 3
}