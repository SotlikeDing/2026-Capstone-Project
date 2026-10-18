namespace MobileApi.Models;

public class PermitResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? Issued { get; set; }
    public DateTime? Expires { get; set; }
}
