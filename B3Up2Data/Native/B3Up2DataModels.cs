namespace StockSharp.B3Up2Data.Native;

sealed class B3AccessToken
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    [JsonProperty("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonProperty("scope")]
    public string Scope { get; set; }
}

sealed class B3SasChannel
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("sas")]
    public string Sas { get; set; }
}

sealed class B3BlobItem
{
    public string Name { get; set; }

    public DateTime? LastModified { get; set; }

    public long? ContentLength { get; set; }

    public string ContentType { get; set; }

    public string ETag { get; set; }
}

sealed class B3BlobPage
{
    public B3BlobItem[] Items { get; set; } = [];

    public string NextMarker { get; set; }
}

sealed class B3DownloadedBlob
{
    public string Name { get; set; }

    public byte[] Content { get; set; } = [];

    public string ContentType { get; set; }

    public DateTime? LastModified { get; set; }

    public string ETag { get; set; }
}

sealed class B3Up2DataApiException :
    InvalidOperationException
{
    public B3Up2DataApiException(
        HttpStatusCode statusCode,
        string requestId,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    public HttpStatusCode StatusCode { get; }

    public string RequestId { get; }
}

readonly record struct B3DatasetDescriptor(
    string Directory,
    string FilePrefix);
