using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Nexi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIModels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Size = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LocalPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DownloadedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SelectedModelId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    UseGPU = table.Column<bool>(type: "bit", nullable: false),
                    SelectedInputDevice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InputSensitivity = table.Column<int>(type: "int", nullable: false),
                    SelectedTheme = table.Column<int>(type: "int", nullable: false),
                    UseSystemAccent = table.Column<bool>(type: "bit", nullable: false),
                    AccentColor = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSettings_AIModels_SelectedModelId",
                        column: x => x.SelectedModelId,
                        principalTable: "AIModels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUser = table.Column<bool>(type: "bit", nullable: false),
                    Context = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AIModels",
                columns: new[] { "Id", "CreatedAt", "Description", "DownloadedDate", "LastModifiedAt", "LocalPath", "Name", "Size", "Status", "Version" },
                values: new object[,]
                {
                    { "llama-13b", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Enhanced version of LLaMA with 13 billion parameters.", null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "LLaMA 13B", "24.1 GB", 0, "2.0.0" },
                    { "llama-7b", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "A foundational large language model with 7 billion parameters.", null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "LLaMA 7B", "13.5 GB", 0, "2.0.0" },
                    { "mistral-7b", new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "High-performance language model optimized for efficiency.", null, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Mistral 7B", "13.8 GB", 0, "1.0.0" }
                });

            migrationBuilder.InsertData(
                table: "UserSettings",
                columns: new[] { "Id", "AccentColor", "InputSensitivity", "LastModifiedAt", "SelectedInputDevice", "SelectedModelId", "SelectedTheme", "UseGPU", "UseSystemAccent" },
                values: new object[] { 1, "#A880E4", 50, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 0, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AIModels_Status",
                table: "AIModels",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SessionId",
                table: "ChatMessages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_Timestamp",
                table: "ChatMessages",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_CreatedAt",
                table: "ChatSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_SelectedModelId",
                table: "UserSettings",
                column: "SelectedModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "AIModels");
        }
    }
}
