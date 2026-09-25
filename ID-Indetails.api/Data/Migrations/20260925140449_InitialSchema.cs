using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ID_Indetails.api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "domains",
                columns: table => new
                {
                    code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domains", x => x.code);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rank = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.code);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "disciplines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disciplines", x => x.id);
                    table.ForeignKey(
                        name: "fk_disciplines_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                columns: table => new
                {
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    year = table.Column<int>(type: "int", nullable: false),
                    last_number = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_sequences", x => new { x.domain_code, x.year });
                    table.CheckConstraint("ck_invoice_sequences_number_non_negative", "`last_number` >= 0");
                    table.ForeignKey(
                        name: "fk_invoice_sequences_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.code);
                    table.ForeignKey(
                        name: "fk_permissions_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_teams_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    permission_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_code, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_code",
                        column: x => x.permission_code,
                        principalTable: "permissions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_code",
                        column: x => x.role_code,
                        principalTable: "roles",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "activity_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    occurred_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    actor_id = table.Column<long>(type: "bigint", nullable: true),
                    project_id = table.Column<long>(type: "bigint", nullable: true),
                    client_id = table.Column<long>(type: "bigint", nullable: true),
                    subject_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    prev_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    payload = table.Column<string>(type: "json", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_demo = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_events_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "billing_settings",
                columns: table => new
                {
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    invoice_prefix = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    default_terms_days = table.Column<int>(type: "int", nullable: false),
                    bank_account = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bank_name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    iban = table.Column<string>(type: "varchar(34)", maxLength: 34, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    swift = table.Column<string>(type: "varchar(11)", maxLength: 11, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    terms_text = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    footer = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_settings", x => x.domain_code);
                    table.CheckConstraint("ck_billing_settings_terms_non_negative", "`default_terms_days` >= 0");
                    table.ForeignKey(
                        name: "fk_billing_settings_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_contacts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    client_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    value = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    normalized_value = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_primary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_whatsapp = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_contacts", x => x.id);
                    table.CheckConstraint("ck_client_contacts_kind", "`kind` IN ('email', 'phone')");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    organisation = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    label = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    archived_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                    table.UniqueConstraint("ak_clients_id_domain_code", x => new { x.id, x.domain_code });
                    table.ForeignKey(
                        name: "fk_clients_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "company_settings",
                columns: table => new
                {
                    id = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    logo_file_id = table.Column<long>(type: "bigint", nullable: true),
                    time_zone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    week_start = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    use24hour_time = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    default_currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    overtime_multiplier = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_settings", x => x.id);
                    table.CheckConstraint("ck_company_settings_overtime_multiplier", "`overtime_multiplier` >= 1");
                    table.CheckConstraint("ck_company_settings_single_row", "`id` = 1");
                    table.CheckConstraint("ck_company_settings_week_start", "`week_start` IN (0, 1)");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "conversation_reads",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    conversation_id = table.Column<long>(type: "bigint", nullable: false),
                    last_read_message_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_reads", x => new { x.user_id, x.conversation_id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    team_id = table.Column<long>(type: "bigint", nullable: true),
                    dm_user_low_id = table.Column<long>(type: "bigint", nullable: true),
                    dm_user_high_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.CheckConstraint("ck_conversations_shape", "(`type` = 'dm' AND `team_id` IS NULL AND `dm_user_low_id` IS NOT NULL AND `dm_user_high_id` IS NOT NULL AND `dm_user_low_id` < `dm_user_high_id`) OR (`type` = 'group' AND `team_id` IS NOT NULL AND `dm_user_low_id` IS NULL AND `dm_user_high_id` IS NULL)");
                    table.CheckConstraint("ck_conversations_type", "`type` IN ('dm', 'group')");
                    table.ForeignKey(
                        name: "fk_conversations_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "domain_settings",
                columns: table => new
                {
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    edit_window = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    allow_overlap = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    allow_future_entries = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    require_description = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    timer_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    log_reminder = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    notify_entry = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    notify_project = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    notify_idle = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    weekly_summary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    hold_logging = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    auto_archive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domain_settings", x => x.domain_code);
                    table.CheckConstraint("ck_domain_settings_edit_window", "`edit_window` IN ('none', 'one_day', 'seven_days', 'unlimited')");
                    table.CheckConstraint("ck_domain_settings_notify_entry", "`notify_entry` IN ('all', 'long', 'digest', 'off')");
                    table.ForeignKey(
                        name: "fk_domain_settings_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rate_per_usd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.CheckConstraint("ck_exchange_rates_rate_positive", "`rate_per_usd` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    storage_key = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    original_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mime_type = table.Column<string>(type: "varchar(127)", maxLength: 127, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.CheckConstraint("ck_files_size_non_negative", "`size_bytes` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    password_hash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    first_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    phone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    role_code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    team_id = table.Column<long>(type: "bigint", nullable: true),
                    position = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    photo_file_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    must_change_password = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_seen_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_active_has_credentials", "`status` <> 'active' OR (`password_hash` IS NOT NULL AND `first_name` IS NOT NULL AND `last_name` IS NOT NULL)");
                    table.CheckConstraint("ck_users_status", "`status` IN ('invited', 'active', 'deactivated')");
                    table.CheckConstraint("ck_users_super_admin_has_no_domain", "(`role_code` = 'super_admin') = (`domain_code` IS NULL)");
                    table.ForeignKey(
                        name: "fk_users_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_files_photo_file_id",
                        column: x => x.photo_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_roles_role_code",
                        column: x => x.role_code,
                        principalTable: "roles",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "overhead_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cost_group = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overhead_items", x => x.id);
                    table.CheckConstraint("ck_overhead_items_cost_group", "`cost_group` IN ('software', 'operating')");
                    table.CheckConstraint("ck_overhead_items_end_after_start", "`ended_on` IS NULL OR `ended_on` >= `started_on`");
                    table.ForeignKey(
                        name: "fk_overhead_items_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    used_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    domain_code = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    icon = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tint = table.Column<string>(type: "varchar(9)", maxLength: 9, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    billing_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    client_id = table.Column<long>(type: "bigint", nullable: true),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    revenue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    revenue_currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    revenue_locked_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    finalized_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    markup = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: true),
                    price_set_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    progress_pct = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.CheckConstraint("ck_projects_billing_type", "`billing_type` IN ('client', 'internal')");
                    table.CheckConstraint("ck_projects_internal_has_no_client_or_revenue", "`billing_type` = 'client' OR (`client_id` IS NULL AND `revenue` = 0)");
                    table.CheckConstraint("ck_projects_markup_non_negative", "`markup` IS NULL OR `markup` >= 0");
                    table.CheckConstraint("ck_projects_progress_range", "`progress_pct` <= 100");
                    table.CheckConstraint("ck_projects_revenue_non_negative", "`revenue` >= 0");
                    table.CheckConstraint("ck_projects_status", "`status` IN ('active', 'hold', 'completed', 'archived')");
                    table.ForeignKey(
                        name: "fk_projects_clients_client_id_domain_code",
                        columns: x => new { x.client_id, x.domain_code },
                        principalTable: "clients",
                        principalColumns: new[] { "id", "domain_code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_projects_domains_domain_code",
                        column: x => x.domain_code,
                        principalTable: "domains",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_projects_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_projects_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    replaced_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_board_layouts",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    board_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    layout = table.Column<string>(type: "json", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    saved_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_board_layouts", x => new { x.user_id, x.board_key });
                    table.ForeignKey(
                        name: "fk_user_board_layouts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    expires_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    accepted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    invited_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_invitations_users_invited_by_id",
                        column: x => x.invited_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_invitations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_pay_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    basis = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hourly_cost_rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_pay_history", x => x.id);
                    table.CheckConstraint("ck_user_pay_history_amount_positive", "`amount` > 0");
                    table.CheckConstraint("ck_user_pay_history_basis", "`basis` IN ('hourly', 'monthly')");
                    table.CheckConstraint("ck_user_pay_history_rate_non_negative", "`hourly_cost_rate` >= 0");
                    table.ForeignKey(
                        name: "fk_user_pay_history_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_pay_history_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "overhead_amounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    overhead_item_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overhead_amounts", x => x.id);
                    table.CheckConstraint("ck_overhead_amounts_amount_non_negative", "`amount` >= 0");
                    table.ForeignKey(
                        name: "fk_overhead_amounts_overhead_items_overhead_item_id",
                        column: x => x.overhead_item_id,
                        principalTable: "overhead_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_overhead_amounts_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    terms_days = table.Column<int>(type: "int", nullable: false),
                    notes = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bill_to_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bill_to_contact = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    issued_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    due_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    voided_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    void_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    voided_by_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_issued_has_due_date", "`issued_at` IS NULL OR `due_at` IS NOT NULL");
                    table.CheckConstraint("ck_invoices_numbered_when_issued", "(`number` IS NULL) = (`issued_at` IS NULL)");
                    table.CheckConstraint("ck_invoices_terms_non_negative", "`terms_days` >= 0");
                    table.CheckConstraint("ck_invoices_total_non_negative", "`total` >= 0");
                    table.ForeignKey(
                        name: "fk_invoices_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "labour_weeks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    submitted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    submitted_by_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labour_weeks", x => x.id);
                    table.CheckConstraint("ck_labour_weeks_starts_on_monday", "WEEKDAY(`week_start`) = 0");
                    table.ForeignKey(
                        name: "fk_labour_weeks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_labour_weeks_users_submitted_by_id",
                        column: x => x.submitted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    recipient_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    actor_id = table.Column<long>(type: "bigint", nullable: true),
                    project_id = table.Column<long>(type: "bigint", nullable: true),
                    subject_type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    subject_id = table.Column<long>(type: "bigint", nullable: true),
                    payload = table.Column<string>(type: "json", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    digest_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_count_positive", "`count` >= 1");
                    table.ForeignKey(
                        name: "fk_notifications_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_expenses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    spent_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_expenses", x => x.id);
                    table.CheckConstraint("ck_project_expenses_amount_non_negative", "`amount` >= 0");
                    table.ForeignKey(
                        name: "fk_project_expenses_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_expenses_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_labour_defaults",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    workers = table.Column<int>(type: "int", nullable: false),
                    daily_pay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_labour_defaults", x => x.id);
                    table.CheckConstraint("ck_project_labour_defaults_pay_non_negative", "`daily_pay` >= 0");
                    table.CheckConstraint("ck_project_labour_defaults_workers_non_negative", "`workers` >= 0");
                    table.ForeignKey(
                        name: "fk_project_labour_defaults_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_labour_defaults_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_materials",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    quantity = table.Column<decimal>(type: "decimal(14,3)", precision: 14, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    unit_cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    supplier = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "ROUND(`quantity` * `unit_cost`, 2)", stored: true),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_materials", x => x.id);
                    table.CheckConstraint("ck_project_materials_quantity_positive", "`quantity` > 0");
                    table.CheckConstraint("ck_project_materials_unit_cost_non_negative", "`unit_cost` >= 0");
                    table.ForeignKey(
                        name: "fk_project_materials_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_materials_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_meetings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    starts_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    ends_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    topic = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_meetings", x => x.id);
                    table.CheckConstraint("ck_project_meetings_ends_after_start", "`ends_at` > `starts_at`");
                    table.ForeignKey(
                        name: "fk_project_meetings_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_meetings_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    discipline_id = table.Column<long>(type: "bigint", nullable: true),
                    added_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_members", x => new { x.project_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_project_members_disciplines_discipline_id",
                        column: x => x.discipline_id,
                        principalTable: "disciplines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_added_by_id",
                        column: x => x.added_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_requirements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    is_done = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    done_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    done_by_id = table.Column<long>(type: "bigint", nullable: true),
                    auto_done = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_requirements", x => x.id);
                    table.CheckConstraint("ck_project_requirements_done_state", "(`is_done` = 1) = (`done_at` IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_project_requirements_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_requirements_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_requirements_users_done_by_id",
                        column: x => x.done_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "work_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "text", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    duration_minutes = table.Column<int>(type: "int", nullable: false, computedColumnSql: "TIMESTAMPDIFF(MINUTE, `started_at`, `ended_at`)", stored: true),
                    attachment_file_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    deleted_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_entries", x => x.id);
                    table.CheckConstraint("ck_work_entries_ends_after_start", "`ended_at` > `started_at`");
                    table.ForeignKey(
                        name: "fk_work_entries_files_attachment_file_id",
                        column: x => x.attachment_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_entries_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoice_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recipient = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    sent_by_id = table.Column<long>(type: "bigint", nullable: true),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_deliveries", x => x.id);
                    table.CheckConstraint("ck_invoice_deliveries_channel", "`channel` IN ('email', 'whatsapp', 'download')");
                    table.ForeignKey(
                        name: "fk_invoice_deliveries_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoice_deliveries_users_sent_by_id",
                        column: x => x.sent_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_invoice_lines_amount_positive", "`amount` > 0");
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    client_id = table.Column<long>(type: "bigint", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fx_rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    amount_usd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    recorded_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false),
                    recorded_by_id = table.Column<long>(type: "bigint", nullable: true),
                    voided_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: true),
                    void_reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    voided_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_positive", "`amount` > 0");
                    table.CheckConstraint("ck_payments_amount_usd_positive", "`amount_usd` > 0");
                    table.CheckConstraint("ck_payments_fx_rate_positive", "`fx_rate` > 0");
                    table.CheckConstraint("ck_payments_method", "`method` IN ('bank', 'cheque', 'cash', 'other')");
                    table.ForeignKey(
                        name: "fk_payments_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_recorded_by_id",
                        column: x => x.recorded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_voided_by_id",
                        column: x => x.voided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "labour_week_days",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    labour_week_id = table.Column<long>(type: "bigint", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    workers = table.Column<int>(type: "int", nullable: false),
                    daily_pay = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "`workers` * `daily_pay`", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_labour_week_days", x => x.id);
                    table.CheckConstraint("ck_labour_week_days_pay_non_negative", "`daily_pay` >= 0");
                    table.CheckConstraint("ck_labour_week_days_workers_non_negative", "`workers` >= 0");
                    table.ForeignKey(
                        name: "fk_labour_week_days_labour_weeks_labour_week_id",
                        column: x => x.labour_week_id,
                        principalTable: "labour_weeks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "requirement_assignees",
                columns: table => new
                {
                    requirement_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requirement_assignees", x => new { x.requirement_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_requirement_assignees_project_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalTable: "project_requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_requirement_assignees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "message_attachments",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    file_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_attachments", x => new { x.message_id, x.file_id });
                    table.ForeignKey(
                        name: "fk_message_attachments_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "message_mentions",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_mentions", x => new { x.message_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_message_mentions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    conversation_id = table.Column<long>(type: "bigint", nullable: false),
                    sender_id = table.Column<long>(type: "bigint", nullable: false),
                    body = table.Column<string>(type: "text", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ref_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    ref_comment_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_users_sender_id",
                        column: x => x.sender_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_messages_work_entries_ref_entry_id",
                        column: x => x.ref_entry_id,
                        principalTable: "work_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "work_entry_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    work_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    body = table.Column<string>(type: "text", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sent_to_group = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_entry_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_entry_comments_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_entry_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_entry_comments_work_entries_work_entry_id",
                        column: x => x.work_entry_id,
                        principalTable: "work_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "domains",
                columns: new[] { "code", "name" },
                values: new object[,]
                {
                    { "design", "Design" },
                    { "execution", "Execution" }
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "activity:view", "View the domain activity feed", null },
                    { "billing_settings:manage", "Edit invoice prefix, terms and bank details", null },
                    { "client:create", "Add clients", null },
                    { "client:manage", "Edit, archive, restore and delete clients", null },
                    { "client:view", "View clients", null },
                    { "company_settings:manage", "Edit company-wide settings", null },
                    { "company:view", "View the company overview across both domains", null },
                    { "domain_settings:manage", "Edit the domain's time-tracking and notification settings", null },
                    { "employee:invite", "Invite new people", null },
                    { "employee:manage", "Edit employee profiles, teams and status", null },
                    { "employee:pay_manage", "Set and change employee pay", null },
                    { "employee:view", "View employees and their work", null },
                    { "expense:manage", "Add and remove project expenses", null },
                    { "financial:view", "See revenue, cost, profit, margin and rates", null },
                    { "invoice:create", "Create and edit draft invoices", null },
                    { "invoice:send", "Issue and resend invoices", null },
                    { "invoice:void", "Void issued invoices", null },
                    { "overhead:manage", "Manage company overhead", null },
                    { "payment:record", "Record client payments", null },
                    { "payment:void", "Void recorded payments", null },
                    { "project:change_status", "Put projects on hold, complete, reopen or archive them", null },
                    { "project:create", "Create projects", null },
                    { "project:edit", "Edit project details, owner, client and revenue", null },
                    { "project:view", "View projects", null },
                    { "report:view", "View domain reports", null },
                    { "requirement:manage", "Add, edit, assign, tick and remove requirements", null },
                    { "requirement:view", "View project requirements", null },
                    { "salary:view", "View what each employee is owed", null }
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "code", "name", "rank" },
                values: new object[,]
                {
                    { "admin", "Admin", (byte)2 },
                    { "employee", "Employee", (byte)1 },
                    { "super_admin", "Super admin", (byte)3 }
                });

            migrationBuilder.InsertData(
                table: "permissions",
                columns: new[] { "code", "description", "domain_code" },
                values: new object[,]
                {
                    { "labour_report:create", "Submit weekly ground-labour reports", "execution" },
                    { "labour_report:manage", "Edit anyone's labour reports and labour defaults", "execution" },
                    { "material:create", "Record materials on a site project", "execution" },
                    { "material:manage", "Edit or remove anyone's material records", "execution" },
                    { "meeting:manage", "Record and remove project meetings", "design" },
                    { "project:set_price", "Set the cost-plus price of an Execution project", "execution" },
                    { "work_entry:comment", "Comment on work entries", "design" },
                    { "work_entry:create", "Log, edit and delete own work entries", "design" },
                    { "work_entry:view_all", "View everyone's work entries", "design" },
                    { "work_entry:view_own", "View own work entries", "design" }
                });

            migrationBuilder.InsertData(
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code" },
                values: new object[,]
                {
                    { "activity:view", "admin" },
                    { "billing_settings:manage", "admin" },
                    { "client:create", "admin" },
                    { "client:manage", "admin" },
                    { "client:view", "admin" },
                    { "domain_settings:manage", "admin" },
                    { "employee:invite", "admin" },
                    { "employee:manage", "admin" },
                    { "employee:pay_manage", "admin" },
                    { "employee:view", "admin" },
                    { "expense:manage", "admin" },
                    { "financial:view", "admin" },
                    { "invoice:create", "admin" },
                    { "invoice:send", "admin" },
                    { "invoice:void", "admin" },
                    { "payment:record", "admin" },
                    { "payment:void", "admin" },
                    { "project:change_status", "admin" },
                    { "project:create", "admin" },
                    { "project:edit", "admin" },
                    { "project:view", "admin" },
                    { "report:view", "admin" },
                    { "requirement:manage", "admin" },
                    { "requirement:view", "admin" },
                    { "project:view", "employee" },
                    { "requirement:view", "employee" },
                    { "activity:view", "super_admin" },
                    { "billing_settings:manage", "super_admin" },
                    { "client:create", "super_admin" },
                    { "client:manage", "super_admin" },
                    { "client:view", "super_admin" },
                    { "company_settings:manage", "super_admin" },
                    { "company:view", "super_admin" },
                    { "domain_settings:manage", "super_admin" },
                    { "employee:invite", "super_admin" },
                    { "employee:manage", "super_admin" },
                    { "employee:pay_manage", "super_admin" },
                    { "employee:view", "super_admin" },
                    { "expense:manage", "super_admin" },
                    { "financial:view", "super_admin" },
                    { "invoice:create", "super_admin" },
                    { "invoice:send", "super_admin" },
                    { "invoice:void", "super_admin" },
                    { "overhead:manage", "super_admin" },
                    { "payment:record", "super_admin" },
                    { "payment:void", "super_admin" },
                    { "project:change_status", "super_admin" },
                    { "project:create", "super_admin" },
                    { "project:edit", "super_admin" },
                    { "project:view", "super_admin" },
                    { "report:view", "super_admin" },
                    { "requirement:manage", "super_admin" },
                    { "requirement:view", "super_admin" },
                    { "salary:view", "super_admin" },
                    { "labour_report:create", "admin" },
                    { "labour_report:manage", "admin" },
                    { "material:create", "admin" },
                    { "material:manage", "admin" },
                    { "meeting:manage", "admin" },
                    { "project:set_price", "admin" },
                    { "work_entry:comment", "admin" },
                    { "work_entry:create", "admin" },
                    { "work_entry:view_all", "admin" },
                    { "work_entry:view_own", "admin" },
                    { "labour_report:create", "employee" },
                    { "material:create", "employee" },
                    { "work_entry:create", "employee" },
                    { "work_entry:view_own", "employee" },
                    { "labour_report:create", "super_admin" },
                    { "labour_report:manage", "super_admin" },
                    { "material:create", "super_admin" },
                    { "material:manage", "super_admin" },
                    { "meeting:manage", "super_admin" },
                    { "project:set_price", "super_admin" },
                    { "work_entry:comment", "super_admin" },
                    { "work_entry:create", "super_admin" },
                    { "work_entry:view_all", "super_admin" },
                    { "work_entry:view_own", "super_admin" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_actor_id",
                table: "activity_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_client_id",
                table: "activity_events",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_domain_code_occurred_at",
                table: "activity_events",
                columns: new[] { "domain_code", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_kind",
                table: "activity_events",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_activity_events_project_id_occurred_at",
                table: "activity_events",
                columns: new[] { "project_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_billing_settings_updated_by_id",
                table: "billing_settings",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_contacts_client_id",
                table: "client_contacts",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_client_contacts_kind_normalized_value",
                table: "client_contacts",
                columns: new[] { "kind", "normalized_value" });

            migrationBuilder.CreateIndex(
                name: "ix_clients_created_by_id",
                table: "clients",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_clients_domain_code",
                table: "clients",
                column: "domain_code");

            migrationBuilder.CreateIndex(
                name: "ix_company_settings_logo_file_id",
                table: "company_settings",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_settings_updated_by_id",
                table: "company_settings",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_reads_conversation_id",
                table: "conversation_reads",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_reads_last_read_message_id",
                table: "conversation_reads",
                column: "last_read_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_dm_user_high_id",
                table: "conversations",
                column: "dm_user_high_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_dm_user_low_id_dm_user_high_id",
                table: "conversations",
                columns: new[] { "dm_user_low_id", "dm_user_high_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_domain_code",
                table: "conversations",
                column: "domain_code");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_team_id",
                table: "conversations",
                column: "team_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disciplines_domain_code_name",
                table: "disciplines",
                columns: new[] { "domain_code", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_domain_settings_updated_by_id",
                table: "domain_settings",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_created_by_id",
                table: "exchange_rates",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_exchange_rates_currency_effective_from",
                table: "exchange_rates",
                columns: new[] { "currency", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_storage_key",
                table: "files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_uploaded_by_id",
                table: "files",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_deliveries_invoice_id",
                table: "invoice_deliveries",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_deliveries_sent_by_id",
                table: "invoice_deliveries",
                column: "sent_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id_position",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_client_id",
                table: "invoices",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_by_id",
                table: "invoices",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_number",
                table: "invoices",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_project_id",
                table: "invoices",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_voided_by_id",
                table: "invoices",
                column: "voided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_labour_week_days_labour_week_id_work_date",
                table: "labour_week_days",
                columns: new[] { "labour_week_id", "work_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_labour_weeks_project_id_week_start",
                table: "labour_weeks",
                columns: new[] { "project_id", "week_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_labour_weeks_submitted_by_id",
                table: "labour_weeks",
                column: "submitted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_attachments_file_id",
                table: "message_attachments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_message_mentions_user_id",
                table: "message_mentions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_id",
                table: "messages",
                columns: new[] { "conversation_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_ref_comment_id",
                table: "messages",
                column: "ref_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_ref_entry_id",
                table: "messages",
                column: "ref_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_sender_id",
                table: "messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_actor_id",
                table: "notifications",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_project_id",
                table: "notifications",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_created_at",
                table: "notifications",
                columns: new[] { "recipient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_id_digest_key",
                table: "notifications",
                columns: new[] { "recipient_id", "digest_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_overhead_amounts_created_by_id",
                table: "overhead_amounts",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_overhead_amounts_overhead_item_id_effective_from",
                table: "overhead_amounts",
                columns: new[] { "overhead_item_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_overhead_items_created_by_id",
                table: "overhead_items",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_client_id",
                table: "payments",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_id",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_project_id",
                table: "payments",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_recorded_by_id",
                table: "payments",
                column: "recorded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_voided_by_id",
                table: "payments",
                column: "voided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_permissions_domain_code",
                table: "permissions",
                column: "domain_code");

            migrationBuilder.CreateIndex(
                name: "ix_project_expenses_created_by_id",
                table: "project_expenses",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_expenses_project_id",
                table: "project_expenses",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_labour_defaults_created_by_id",
                table: "project_labour_defaults",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_labour_defaults_project_id_effective_from",
                table: "project_labour_defaults",
                columns: new[] { "project_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_materials_added_by_id",
                table: "project_materials",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_materials_project_id",
                table: "project_materials",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_meetings_created_by_id",
                table: "project_meetings",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_meetings_project_id_starts_at",
                table: "project_meetings",
                columns: new[] { "project_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_members_added_by_id",
                table: "project_members",
                column: "added_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_discipline_id",
                table: "project_members",
                column: "discipline_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_members_user_id",
                table: "project_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_requirements_created_by_id",
                table: "project_requirements",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_requirements_done_by_id",
                table: "project_requirements",
                column: "done_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_requirements_project_id",
                table: "project_requirements",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_client_id_domain_code",
                table: "projects",
                columns: new[] { "client_id", "domain_code" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_created_by_id",
                table: "projects",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_domain_code_status",
                table: "projects",
                columns: new[] { "domain_code", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_owner_id",
                table: "projects",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_replaced_by_id",
                table: "refresh_tokens",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_assignees_user_id",
                table: "requirement_assignees",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_code",
                table: "role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_teams_domain_code_name",
                table: "teams",
                columns: new[] { "domain_code", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_invitations_invited_by_id",
                table: "user_invitations",
                column: "invited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_invitations_token_hash",
                table: "user_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_invitations_user_id",
                table: "user_invitations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_pay_history_created_by_id",
                table: "user_pay_history",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_pay_history_user_id_effective_from",
                table: "user_pay_history",
                columns: new[] { "user_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_created_by_id",
                table: "users",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_domain_code",
                table: "users",
                column: "domain_code");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_photo_file_id",
                table: "users",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_role_code",
                table: "users",
                column: "role_code");

            migrationBuilder.CreateIndex(
                name: "ix_users_team_id",
                table: "users",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_entries_attachment_file_id",
                table: "work_entries",
                column: "attachment_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_entries_project_id_started_at",
                table: "work_entries",
                columns: new[] { "project_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_entries_user_id_started_at",
                table: "work_entries",
                columns: new[] { "user_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_entry_comments_author_id",
                table: "work_entry_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_entry_comments_message_id",
                table: "work_entry_comments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_entry_comments_work_entry_id",
                table: "work_entry_comments",
                column: "work_entry_id");

            migrationBuilder.AddForeignKey(
                name: "fk_activity_events_clients_client_id",
                table: "activity_events",
                column: "client_id",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_events_projects_project_id",
                table: "activity_events",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_activity_events_users_actor_id",
                table: "activity_events",
                column: "actor_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_billing_settings_users_updated_by_id",
                table: "billing_settings",
                column: "updated_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_client_contacts_clients_client_id",
                table: "client_contacts",
                column: "client_id",
                principalTable: "clients",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_clients_users_created_by_id",
                table: "clients",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_company_settings_files_logo_file_id",
                table: "company_settings",
                column: "logo_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_company_settings_users_updated_by_id",
                table: "company_settings",
                column: "updated_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_reads_conversations_conversation_id",
                table: "conversation_reads",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_reads_messages_last_read_message_id",
                table: "conversation_reads",
                column: "last_read_message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversation_reads_users_user_id",
                table: "conversation_reads",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_users_dm_user_high_id",
                table: "conversations",
                column: "dm_user_high_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_users_dm_user_low_id",
                table: "conversations",
                column: "dm_user_low_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_domain_settings_users_updated_by_id",
                table: "domain_settings",
                column: "updated_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_exchange_rates_users_created_by_id",
                table: "exchange_rates",
                column: "created_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_files_users_uploaded_by_id",
                table: "files",
                column: "uploaded_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_message_attachments_messages_message_id",
                table: "message_attachments",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_message_mentions_messages_message_id",
                table: "message_mentions",
                column: "message_id",
                principalTable: "messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_messages_work_entry_comments_ref_comment_id",
                table: "messages",
                column: "ref_comment_id",
                principalTable: "work_entry_comments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_projects_clients_client_id_domain_code",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_domains_domain_code",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_projects_domains_domain_code",
                table: "projects");

            migrationBuilder.DropForeignKey(
                name: "fk_teams_domains_domain_code",
                table: "teams");

            migrationBuilder.DropForeignKey(
                name: "fk_users_domains_domain_code",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "fk_work_entries_projects_project_id",
                table: "work_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_users_dm_user_high_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_conversations_users_dm_user_low_id",
                table: "conversations");

            migrationBuilder.DropForeignKey(
                name: "fk_files_users_uploaded_by_id",
                table: "files");

            migrationBuilder.DropForeignKey(
                name: "fk_messages_users_sender_id",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "fk_work_entries_users_user_id",
                table: "work_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_work_entry_comments_users_author_id",
                table: "work_entry_comments");

            migrationBuilder.DropForeignKey(
                name: "fk_work_entries_files_attachment_file_id",
                table: "work_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_messages_conversations_conversation_id",
                table: "messages");

            migrationBuilder.DropForeignKey(
                name: "fk_work_entry_comments_messages_message_id",
                table: "work_entry_comments");

            migrationBuilder.DropTable(
                name: "activity_events");

            migrationBuilder.DropTable(
                name: "billing_settings");

            migrationBuilder.DropTable(
                name: "client_contacts");

            migrationBuilder.DropTable(
                name: "company_settings");

            migrationBuilder.DropTable(
                name: "conversation_reads");

            migrationBuilder.DropTable(
                name: "domain_settings");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "invoice_deliveries");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "invoice_sequences");

            migrationBuilder.DropTable(
                name: "labour_week_days");

            migrationBuilder.DropTable(
                name: "message_attachments");

            migrationBuilder.DropTable(
                name: "message_mentions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "overhead_amounts");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "project_expenses");

            migrationBuilder.DropTable(
                name: "project_labour_defaults");

            migrationBuilder.DropTable(
                name: "project_materials");

            migrationBuilder.DropTable(
                name: "project_meetings");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "requirement_assignees");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "user_board_layouts");

            migrationBuilder.DropTable(
                name: "user_invitations");

            migrationBuilder.DropTable(
                name: "user_pay_history");

            migrationBuilder.DropTable(
                name: "labour_weeks");

            migrationBuilder.DropTable(
                name: "overhead_items");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "disciplines");

            migrationBuilder.DropTable(
                name: "project_requirements");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "clients");

            migrationBuilder.DropTable(
                name: "domains");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "work_entry_comments");

            migrationBuilder.DropTable(
                name: "work_entries");
        }
    }
}
