using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LeaversForm.Data.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AuthorisingManagers", table => new
        {
            Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            DisplayName = table.Column<string>(maxLength: 200, nullable: false),
            Email = table.Column<string>(maxLength: 320, nullable: false),
            IsActive = table.Column<bool>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_AuthorisingManagers", x => x.Id));
        migrationBuilder.CreateTable("LeaverRequests", table => new
        {
            Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            LeaverEntraId = table.Column<string>(maxLength: 100, nullable: false),
            LeaverDisplayName = table.Column<string>(maxLength: 200, nullable: false),
            LeaverUserPrincipalName = table.Column<string>(maxLength: 320, nullable: false),
            LeaverEmail = table.Column<string>(maxLength: 320, nullable: false),
            SamAccountName = table.Column<string>(maxLength: 100, nullable: true),
            Department = table.Column<string>(maxLength: 200, nullable: true),
            LeavingDate = table.Column<DateOnly>(nullable: false),
            EquipmentNotes = table.Column<string>(maxLength: 4000, nullable: false),
            Status = table.Column<string>(maxLength: 30, nullable: false),
            SubmittedByName = table.Column<string>(maxLength: 200, nullable: false),
            SubmittedByEmail = table.Column<string>(maxLength: 320, nullable: false),
            SubmittedAt = table.Column<DateTime>(nullable: false),
            LastUpdatedByName = table.Column<string>(nullable: true),
            LastUpdatedAt = table.Column<DateTime>(nullable: true),
            ScriptEligibleAt = table.Column<DateTime>(nullable: false),
            ScriptExecutedAt = table.Column<DateTime>(nullable: true),
            ScriptExecutionStatus = table.Column<string>(maxLength: 30, nullable: false),
            ScriptExecutionLogPath = table.Column<string>(nullable: true),
            CancelledAt = table.Column<DateTime>(nullable: true),
            CancelledByName = table.Column<string>(nullable: true),
            CancelledByEmail = table.Column<string>(nullable: true)
        }, constraints: table => table.PrimaryKey("PK_LeaverRequests", x => x.Id));
        migrationBuilder.CreateTable("LeaverRequestAuthorisingManagers", table => new
        {
            LeaverRequestId = table.Column<int>(nullable: false),
            AuthorisingManagerId = table.Column<int>(nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_LeaverRequestAuthorisingManagers", x => new { x.LeaverRequestId, x.AuthorisingManagerId }); });
        migrationBuilder.CreateTable("AuditLogs", table => new
        {
            Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            Action = table.Column<string>(maxLength: 80, nullable: false),
            LeaverRequestId = table.Column<int>(nullable: true),
            PerformedByName = table.Column<string>(maxLength: 200, nullable: false),
            PerformedByEmail = table.Column<string>(maxLength: 320, nullable: false),
            PerformedAt = table.Column<DateTime>(nullable: false),
            OldValue = table.Column<string>(nullable: true),
            NewValue = table.Column<string>(nullable: true),
            Notes = table.Column<string>(maxLength: 4000, nullable: true)
        }, constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));
        migrationBuilder.InsertData("AuthorisingManagers", new[] { "Id", "DisplayName", "Email", "IsActive" }, new object[,]
        {
            {1,"Antony Talbot","antony.talbot@yoursclothing.co.uk",true},{2,"Beth Watson","beth.watson@yoursclothing.co.uk",true},{3,"Claire Atkinson","claire.atkinson@yoursclothing.co.uk",true},{4,"David Preece","david.preece@yoursclothing.co.uk",true},{5,"Hannah Quy","hannah.quy@yoursclothing.co.uk",true},{6,"Harley Killingsworth","harley.killingsworth@yoursclothing.co.uk",true},{7,"Kay Clay","kay.clay@yoursclothing.co.uk",true},{8,"Leigh Porter","leigh.porter@yoursclothing.co.uk",true},{9,"Peter Rodgers","peter.rodgers@yoursclothing.co.uk",true},{10,"Monika Tyton","monika.tyton@yoursclothing.co.uk",true},{11,"Patrycja Lyszczarl","patrycja.lyszczarl@yoursclothing.co.uk",true}
        });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuditLogs"); migrationBuilder.DropTable("LeaverRequestAuthorisingManagers"); migrationBuilder.DropTable("LeaverRequests"); migrationBuilder.DropTable("AuthorisingManagers");
    }
}
