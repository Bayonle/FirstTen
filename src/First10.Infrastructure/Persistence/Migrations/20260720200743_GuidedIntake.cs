using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuidedIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "identity_audit",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "guided_sessions",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReporterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContactReference = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LocationReminderDueAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LocationReminderSentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_intents",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactReference = table.Column<Guid>(type: "uuid", nullable: false),
                    Prompt = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CatalogueVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_intents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reporter_contacts",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    ReporterKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedDestination = table.Column<string>(type: "text", nullable: false),
                    EncryptionKeyVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporter_contacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "guided_session_inputs",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ContentKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderMediaHandle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_session_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_guided_session_inputs_guided_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "intake",
                        principalTable: "guided_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_guided_session_inputs_SessionId_ProviderMessageId",
                schema: "intake",
                table: "guided_session_inputs",
                columns: new[] { "SessionId", "ProviderMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guided_sessions_Channel_ReporterKey_OpenedAtUtc",
                schema: "intake",
                table: "guided_sessions",
                columns: new[] { "Channel", "ReporterKey", "OpenedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_prompt_intents_SessionId_Prompt",
                schema: "intake",
                table: "prompt_intents",
                columns: new[] { "SessionId", "Prompt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reporter_contacts_Channel_ReporterKey",
                schema: "intake",
                table: "reporter_contacts",
                columns: new[] { "Channel", "ReporterKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "identity_audit");

            migrationBuilder.DropTable(
                name: "guided_session_inputs",
                schema: "intake");

            migrationBuilder.DropTable(
                name: "prompt_intents",
                schema: "intake");

            migrationBuilder.DropTable(
                name: "reporter_contacts",
                schema: "intake");

            migrationBuilder.DropTable(
                name: "guided_sessions",
                schema: "intake");
        }
    }
}
