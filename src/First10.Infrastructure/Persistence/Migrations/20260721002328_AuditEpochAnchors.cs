using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEpochAnchors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_anchors",
                schema: "identity_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecordedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_anchors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_anchors_LastSequence",
                schema: "identity_audit",
                table: "audit_anchors",
                column: "LastSequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_anchors",
                schema: "identity_audit");
        }
    }
}
