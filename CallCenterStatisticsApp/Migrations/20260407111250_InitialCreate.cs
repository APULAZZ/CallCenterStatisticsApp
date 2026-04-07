using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenterStatisticsApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MangoGroupId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallStatusRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatusCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StatusText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CountAsAnswered = table.Column<bool>(type: "bit", nullable: false),
                    CountAsMissedIncoming = table.Column<bool>(type: "bit", nullable: false),
                    CountAsOutgoingNoAnswer = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallStatusRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MangoTopicId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallTopics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MangoUserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MangoUserKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MangoSyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SyncType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PeriodTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImportedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false),
                    ErrorText = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MangoSyncLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CallRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MangoCallId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CallDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    TopicId = table.Column<int>(type: "int", nullable: true),
                    ExternalPhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StatusCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StatusText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    TalkDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    WaitDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    IsIncoming = table.Column<bool>(type: "bit", nullable: false),
                    IsOutgoing = table.Column<bool>(type: "bit", nullable: false),
                    IsAnswered = table.Column<bool>(type: "bit", nullable: false),
                    IsMissedIncoming = table.Column<bool>(type: "bit", nullable: false),
                    IsOutgoingNoAnswer = table.Column<bool>(type: "bit", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallRecords_CallGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "CallGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CallRecords_CallTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "CallTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CallRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    DateFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateTo = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeGroups_CallGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "CallGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeGroups_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallGroups_MangoGroupId",
                table: "CallGroups",
                column: "MangoGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_CallDateTime",
                table: "CallRecords",
                column: "CallDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_EmployeeId",
                table: "CallRecords",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_GroupId",
                table: "CallRecords",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_MangoCallId",
                table: "CallRecords",
                column: "MangoCallId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CallRecords_TopicId",
                table: "CallRecords",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_CallStatusRules_StatusCode",
                table: "CallStatusRules",
                column: "StatusCode");

            migrationBuilder.CreateIndex(
                name: "IX_CallTopics_MangoTopicId",
                table: "CallTopics",
                column: "MangoTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeGroups_EmployeeId",
                table: "EmployeeGroups",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeGroups_GroupId",
                table: "EmployeeGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_MangoUserId",
                table: "Employees",
                column: "MangoUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "CallRecords");

            migrationBuilder.DropTable(
                name: "CallStatusRules");

            migrationBuilder.DropTable(
                name: "EmployeeGroups");

            migrationBuilder.DropTable(
                name: "MangoSyncLogs");

            migrationBuilder.DropTable(
                name: "CallTopics");

            migrationBuilder.DropTable(
                name: "CallGroups");

            migrationBuilder.DropTable(
                name: "Employees");
        }
    }
}
