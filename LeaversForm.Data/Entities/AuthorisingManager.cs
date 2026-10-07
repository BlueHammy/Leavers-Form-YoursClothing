namespace LeaversForm.Data.Entities;

public class AuthorisingManager
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<LeaverRequestAuthorisingManager> LeaverRequests { get; set; } = new List<LeaverRequestAuthorisingManager>();
}
