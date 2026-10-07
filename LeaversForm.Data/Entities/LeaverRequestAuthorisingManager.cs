namespace LeaversForm.Data.Entities;

public class LeaverRequestAuthorisingManager
{
    public int Id { get; set; }
    public int LeaverRequestId { get; set; }
    public int AuthorisingManagerId { get; set; }
    public LeaverRequest LeaverRequest { get; set; } = null!;
    public AuthorisingManager AuthorisingManager { get; set; } = null!;
}
