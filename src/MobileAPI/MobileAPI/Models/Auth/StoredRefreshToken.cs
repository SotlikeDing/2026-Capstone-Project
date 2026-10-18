namespace MobileApi.Models.Auth;

public class StoredRefreshToken
{
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? IpAddress { get; set; }
}
