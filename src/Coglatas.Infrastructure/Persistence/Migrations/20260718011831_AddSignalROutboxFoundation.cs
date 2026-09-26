using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coglatas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignalROutboxFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PayloadSchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", maxLength: 65536, nullable: false),
                    RoutingJson = table.Column<string>(type: "jsonb", maxLength: 8192, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CausationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockOwner = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    LockToken = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.Id);
                    table.CheckConstraint("CK_outbox_events_attempt_count", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_outbox_events_dead_lettered_at", "\"Status\" <> 'DeadLetter' OR \"DeadLetteredAt\" IS NOT NULL");
                    table.CheckConstraint("CK_outbox_events_delivered_at", "\"Status\" <> 'Delivered' OR \"DeliveredAt\" IS NOT NULL");
                    table.CheckConstraint("CK_outbox_events_lock_fields", "(\"LockedAt\" IS NULL AND \"LockOwner\" IS NULL AND \"LockToken\" IS NULL) OR (\"LockedAt\" IS NOT NULL AND \"LockOwner\" IS NOT NULL AND \"LockToken\" IS NOT NULL)");
                    table.CheckConstraint("CK_outbox_events_payload_schema_version", "\"PayloadSchemaVersion\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_AggregateType_AggregateId_AggregateVersion",
                table: "outbox_events",
                columns: new[] { "AggregateType", "AggregateId", "AggregateVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_CreatedAt",
                table: "outbox_events",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_DeadLetteredAt",
                table: "outbox_events",
                column: "DeadLetteredAt",
                filter: "\"Status\" = 'DeadLetter'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_DeliveredAt",
                table: "outbox_events",
                column: "DeliveredAt",
                filter: "\"Status\" = 'Delivered'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_LockedAt",
                table: "outbox_events",
                column: "LockedAt",
                filter: "\"Status\" = 'Processing'");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_Status_NextAttemptAt_CreatedAt",
                table: "outbox_events",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_TenantId",
                table: "outbox_events",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_events_TenantId_Status_CreatedAt",
                table: "outbox_events",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_events");
        }
    }
}
