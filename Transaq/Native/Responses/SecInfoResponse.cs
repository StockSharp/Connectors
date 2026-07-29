namespace StockSharp.Transaq.Native.Responses;

class SecInfoResponse : BaseResponse
{
	public int SecId { get; set; }
	public int Market { get; set; }
	public string SecCode { get; set; }
	public string SecName { get; set; }
	public string PName { get; set; }
	public DateTime? MatDate { get; set; }
	public double? ClearingPrice { get; set; }
	public double? MinPrice { get; set; }
	public double? MaxPrice { get; set; }
	public double? BuyDeposit { get; set; }
	public double? SellDeposit { get; set; }
	public double? BgoC { get; set; }
	public double? BgoNC { get; set; }
	public double? BgoBuy { get; set; }
	public double? Accruedint { get; set; }
	public double? CouponValue { get; set; }
	public DateTime? CouponDate { get; set; }
	public int? CouponPeriod { get; set; }
	public double? FaceValue { get; set; }
	public SecInfoPutCalls? PutCall { get; set; }
	public SecInfoOptTypes? OptType { get; set; }
	public int? LotVolume { get; set; }
	public string CurrencyId { get; set; }
}

enum SecInfoPutCalls
{
	C,
	P
}

enum SecInfoOptTypes
{
	M,
	P
}