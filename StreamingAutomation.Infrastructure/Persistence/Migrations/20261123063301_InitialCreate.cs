using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StreamingAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "media_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "test_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_sessions = table.Column<int>(type: "integer", nullable: false),
                    max_concurrency = table.Column<int>(type: "integer", nullable: false),
                    ramp_up_seconds = table.Column<int>(type: "integer", nullable: false),
                    playback_speed = table.Column<double>(type: "double precision", nullable: false),
                    simulated_failure_rate = table.Column<double>(type: "double precision", nullable: false),
                    started_count = table.Column<int>(type: "integer", nullable: false),
                    completed_count = table.Column<int>(type: "integer", nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    cancelled_count = table.Column<int>(type: "integer", nullable: false),
                    peak_concurrency = table.Column<int>(type: "integer", nullable: false),
                    total_bytes_received = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_test_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "playback_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    position_seconds = table.Column<double>(type: "double precision", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    bytes_received = table.Column<long>(type: "bigint", nullable: false),
                    segments_received = table.Column<int>(type: "integer", nullable: false),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    playback_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playback_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_playback_sessions_media_items_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_playback_sessions_test_runs_test_run_id",
                        column: x => x.test_run_id,
                        principalTable: "test_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_events_playback_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "playback_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "media_items",
                columns: new[] { "id", "created_at", "duration_seconds", "external_id", "platform", "title" },
                values: new object[,]
                {
                    { new Guid("8f1c2a10-0000-4000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 60, "test-media-01", "YouTube", "Test Media 01 – Short clip" },
                    { new Guid("8f1c2a10-0000-4000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 180, "test-media-02", "YouTube", "Test Media 02 – Music track" },
                    { new Guid("8f1c2a10-0000-4000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 300, "test-media-03", "YouTube", "Test Media 03 – Podcast segment" },
                    { new Guid("8f1c2a10-0000-4000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 240, "test-media-04", "YouTube", "Test Media 04 – Tutorial" },
                    { new Guid("8f1c2a10-0000-4000-8000-000000000005"), new DateTimeOffset(new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 120, "test-media-05", "YouTube", "Test Media 05 – Trailer" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_playback_sessions_media_item_id",
                table: "playback_sessions",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_playback_sessions_test_run_id_sequence_number",
                table: "playback_sessions",
                columns: new[] { "test_run_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_playback_sessions_test_run_id_status",
                table: "playback_sessions",
                columns: new[] { "test_run_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_session_events_session_id_occurred_at",
                table: "session_events",
                columns: new[] { "session_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_session_events_test_run_id_occurred_at",
                table: "session_events",
                columns: new[] { "test_run_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_test_runs_created_at",
                table: "test_runs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_test_runs_status",
                table: "test_runs",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_events");

            migrationBuilder.DropTable(
                name: "playback_sessions");

            migrationBuilder.DropTable(
                name: "media_items");

            migrationBuilder.DropTable(
                name: "test_runs");
        }
    }
}
