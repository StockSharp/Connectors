namespace StockSharp.Bitbank;

static class ErrorHelper
{
	private static readonly Dictionary<int, string> _errorsIds = new()
	{
		{ 10000, "URL does not exist." },
		{ 10001, "A system error occurred. Please contact support." },
		{ 10002, "Invalid JSON format. Please check the contents of transmission." },
		{ 10003, "A system error occurred. Please contact support." },
		{ 10005, "Timeout error occurred. Please wait for a while and try again." },
		{ 20001, "API authentication failed." },
		{ 20002, "API key is invalid." },
		{ 20003, "API key does not exist." },
		{ 20004, "API Nonce does not exist." },
		{ 20005, "API signature does not exist." },
		{ 30001, "Please specify the order quantity." },
		{ 30006, "Please specify order ID." },
		{ 30007, "Please specify order ID sequence." },
		{ 30009, "Please select brand name." },
		{ 30012, "Please specify order price." },
		{ 30013, "Please specify either trading." },
		{ 30015, "Please specify order type." },
		{ 30016, "Please specify asset name." },
		{ 30019, "Please specify uuid." },
		{ 30039, "Please specify your withdrawal amount." },
		{ 40001, "Order quantity is invalid." },
		{ 40006, "count value is invalid." },
		{ 40007, "End time is invalid." },
		{ 40008, "end_id value is invalid." },
		{ 40009, "from_id value is invalid." },
		{ 40013, "Order ID is invalid." },
		{ 40014, "Order ID array is invalid." },
		{ 40015, "Too many orders specified." },
		{ 40017, "Name of brand is invalid." },
		{ 40020, "Order price is invalid." },
		{ 40021, "Trading category is invalid." },
		{ 40022, "Start time is invalid." },
		{ 40024, "Order type is invalid." },
		{ 40025, "asset name is invalid." },
		{ 40028, "uuid is invalid." },
		{ 40048, "Outgoing amount is invalid." },
		{ 50003, "Currently, this account can not perform the operation you specified. Please contact support." },
		{ 50004, "Currently, this account is temporarily registered. Please try again after registering your account." },
		{ 50005, "Currently, this account is locked. Please contact support." },
		{ 50006, "Currently, this account is locked. Please contact support." },
		{ 50008, "User identification has not been completed." },
		{ 50009, "Your order does not exist." },
		{ 50010, "Your order can not be canceled." },
		{ 50011, "API not found." },
		{ 60001, "Number of holdings is insufficient." },
		{ 60002, "The quantity upper limit of the tender buy order is exceeded." },
		{ 60003, "Specified quantity exceeds limit." },
		{ 60004, "Specified quantity is below threshold." },
		{ 60005, "Specified price exceeds the maximum limit." },
		{ 60006, "Specified price is below the lower limit." },
		{ 70001, "A system error occurred. Please contact support." },
		{ 70002, "A system error occurred. Please contact support." },
		{ 70003, "A system error occurred. Please contact support." },
		{ 70004, "We are unable to receive your order because we are currently suspended." },
		{ 70005, "Order can not be accepted{  because currently buying order is suspended." },
		{ 70006, "Current order is suspended, so we can not accept order." },
	};

	public static string ToErrorText(this string errorCode)
	{
		if (errorCode.IsEmpty())
			throw new ArgumentNullException(nameof(errorCode));

		if (!int.TryParse(errorCode, out var id))
			return errorCode;

		return _errorsIds.TryGetValue(id) ?? errorCode;
	}
}