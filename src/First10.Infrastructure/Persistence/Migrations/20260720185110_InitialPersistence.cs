using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "intake");

            migrationBuilder.EnsureSchema(
                name: "incidents");

            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.CreateTable(
                name: "inbound_message_receipts",
                schema: "intake",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DownstreamCommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_message_receipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "incident_locations",
                schema: "incidents",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    HasConflict = table.Column<bool>(type: "boolean", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_locations", x => x.IncidentId);
                });

            migrationBuilder.CreateTable(
                name: "incident_timeline_events",
                schema: "incidents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_timeline_events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "outbound_message_records",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticIdentity = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    EnqueuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_message_records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_message_receipts_Scope_Key",
                schema: "intake",
                table: "inbound_message_receipts",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incident_timeline_events_IncidentId_OccurredAtUtc",
                schema: "incidents",
                table: "incident_timeline_events",
                columns: new[] { "IncidentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_outbound_message_records_SemanticIdentity",
                schema: "messaging",
                table: "outbound_message_records",
                column: "SemanticIdentity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbound_message_receipts",
                schema: "intake");

            migrationBuilder.DropTable(
                name: "incident_locations",
                schema: "incidents");

            migrationBuilder.DropTable(
                name: "incident_timeline_events",
                schema: "incidents");

            migrationBuilder.DropTable(
                name: "outbound_message_records",
                schema: "messaging");
        }
    }
}
