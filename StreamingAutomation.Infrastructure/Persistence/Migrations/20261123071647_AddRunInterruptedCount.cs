using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StreamingAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunInterruptedCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "interrupted_count",
                table: "test_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "interrupted_count",
                table: "test_runs");
        }
    }
}
