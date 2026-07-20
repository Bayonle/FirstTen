using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InputId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SafeObjectKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    SafeContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SafeLength = table.Column<long>(type: "bigint", nullable: true),
                    EncryptionKeyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PrivacyProcessorVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_assets_guided_session_inputs_InputId",
                        column: x => x.InputId,
                        principalSchema: "intake",
                        principalTable: "guided_session_inputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_InputId",
                schema: "intake",
                table: "media_assets",
                column: "InputId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_Status_ExpiresAtUtc",
                schema: "intake",
                table: "media_assets",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "intake");
        }
    }
}
