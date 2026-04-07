using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenterStatisticsApp.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordingIdToCallRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordingId",
                table: "CallRecords",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordingId",
                table: "CallRecords");
        }
    }
}
