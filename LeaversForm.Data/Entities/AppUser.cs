using LeaversForm.Data.Enums;

namespace LeaversForm.Data.Entities;

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public AppRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
