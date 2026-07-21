namespace StockSharp.FalconX.Native;

sealed class FalconXOrderSocketClient : FalconXSocketClient
{
	public FalconXOrderSocketClient(string endpoint,
		FalconXAuthenticator authenticator, WorkingTime workingTime,
		int reconnectAttempts)
		: base(endpoint, authenticator, workingTime, reconnectAttempts)
	{
	}

	public override string Name => "FalconX_Orders_WS";

	public event Func<FalconXSocketResponse<FalconXSocketOrderBody>,
		CancellationToken, ValueTask> OrderReceived;

	public async ValueTask SendOrderAsync(FalconXSocketOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		await EnsureConnectedAsync(cancellationToken);
		await SendAsync(request, cancellationToken);
	}

	protected override async ValueTask OnMessageAsync(string payload,
		FalconXSocketHeader header, CancellationToken cancellationToken)
	{
		if (header.Event is not (
			FalconXSocketEvents.CreateOrderAcknowledged or
			FalconXSocketEvents.CreateOrderAccepted or
			FalconXSocketEvents.CreateOrderRejected or
			FalconXSocketEvents.UpdateOrderAcknowledged or
			FalconXSocketEvents.UpdateOrderAccepted or
			FalconXSocketEvents.UpdateOrderRejected or
			FalconXSocketEvents.CancelOrderAcknowledged or
			FalconXSocketEvents.CancelOrderAccepted or
			FalconXSocketEvents.CancelOrderRejected or
			FalconXSocketEvents.OrderUpdate or
			FalconXSocketEvents.OrderResponse or
			FalconXSocketEvents.OrderRejected or
			FalconXSocketEvents.ErrorResponse))
			return;
		var response =
			Deserialize<FalconXSocketResponse<FalconXSocketOrderBody>>(payload);
		if (OrderReceived is { } handler)
			await handler(response, cancellationToken);
	}
}
