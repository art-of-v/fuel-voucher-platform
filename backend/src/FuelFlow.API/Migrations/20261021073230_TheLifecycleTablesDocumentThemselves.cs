using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelFlow.API.Migrations
{
    /// <inheritdoc />
    public partial class TheLifecycleTablesDocumentThemselves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "user_agent",
                table: "support_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "The free-form browser User-Agent, truncated. Diagnostic only.",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "send_error",
                table: "support_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "The last SMTP failure reason, truncated. NULL when delivery succeeded or was neverattempted.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "support_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                comment: "What they wrote.",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                table: "support_messages",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                comment: "The client IP as seen by the API (after forwarded-header processing). Used to spotabuse; never shown anywhere.",
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "email_sent_at_utc",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: true,
                comment: "NULL until the message was successfully handed to the SMTP server. NULL is normal whenSupportMail is not configured (e.g. dev).",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "support_messages",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                comment: "The reply address of the person who wrote in (validated at the edge).",
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When they wrote in.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "notifications",
                type: "uuid",
                nullable: false,
                comment: "The account this notification is for.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                comment: "When it last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                comment: "The notification's headline.",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                comment: "The notification's body.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<bool>(
                name: "is_read",
                table: "notifications",
                type: "boolean",
                nullable: false,
                comment: "Whether the customer has read it.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was created.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "verdict",
                table: "feedback_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "NULL until the classifier has run. NULL means 'not classified', NOT 'junk' - the two aredifferent facts and the filter has to tell them apart.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "feedback_messages",
                type: "uuid",
                nullable: true,
                comment: "The FuelFlow account this chat was bound to, or NULL for an unbound chat.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "feedback_messages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                comment: "What the customer wrote, capped at the configured maximum length.",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<long>(
                name: "telegram_message_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: false,
                comment: "The id of this message inside that chat. Unique per chat - the webhook de-dupe key.",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_chat_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: false,
                comment: "Telegram chat id of the customer's private chat (positive for a 1-on-1 chat).",
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "summary",
                table: "feedback_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                comment: "The one-sentence restatement the classifier wrote for the staff chat. Never invented.",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "feedback_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "Where the message stands.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "staff_replied_at_utc",
                table: "feedback_messages",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Set once an allowlisted team member's reply was delivered to the customer.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "staff_message_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: true,
                comment: "The id of the staff reply that was relayed.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "staff_chat_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: true,
                comment: "Where this message was posted for the team, and the id it got there under. A staff replyis only relayed when it quotes this id, which is what stops group chatter reachingcustomers.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "feedback_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "How serious the classifier says it is. NULL until it has run.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "feedback_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "The client platform (ios / android / web).",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "language",
                table: "feedback_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "The language the classifier detected. NULL until it has run.",
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "feedback_messages",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When the customer wrote it.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "confidence",
                table: "feedback_messages",
                type: "numeric(5,4)",
                nullable: true,
                comment: "The classifier's confidence, 0..1. Retained even for dropped messages, so a driftingclassifier is visible in the admin filter rather than invisible in the discard bin. Adecimal, not a double: binary floating point cannot represent 0.1 exactly, and aconfidence of 0.6 is a number people read.",
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "classifier_error",
                table: "feedback_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "The last classifier failure, truncated. A row with this set and no verdict is one theteam can still read - it is only the automation that failed.",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "classified_at_utc",
                table: "feedback_messages",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the classifier last ran, so a failed row can be retried or aged out.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "feedback_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                comment: "What the classifier says this is about. NULL until it has run.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "app_version",
                table: "feedback_messages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "The client build the message came from - the first thing asked for in a bug report.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "worker_user_id",
                table: "company_members",
                type: "uuid",
                nullable: false,
                comment: "The person.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "company_members",
                type: "uuid",
                nullable: false,
                comment: "The company.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "joined_at_utc",
                table: "company_members",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When they joined.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "company_members",
                type: "boolean",
                nullable: false,
                comment: "Firing a worker sets this rather than removing the row. Employment history IS history:the owner policy keeps anything that affects it forever, and 'this person worked hereuntil March' is a fact about the company, not garbage a cleanup job may collect. The!IsDeleted query filter keeps it out of every membership read.",
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<DateTime>(
                name: "deleted_at_utc",
                table: "company_members",
                type: "timestamp with time zone",
                nullable: true,
                comment: "When the membership was soft-deleted, or NULL while it stands.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "worker_user_id",
                table: "company_invitations",
                type: "uuid",
                nullable: false,
                comment: "The account the invitation names, once it is accepted.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "worker_phone_number",
                table: "company_invitations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "The phone number the invitation was sent to.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "company_invitations",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it last changed.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "company_invitations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                comment: "Where the invitation stands.",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<Guid>(
                name: "owner_user_id",
                table: "company_invitations",
                type: "uuid",
                nullable: false,
                comment: "Who sent the invitation.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "company_invitations",
                type: "uuid",
                nullable: false,
                comment: "The company that invited.",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "company_invitations",
                type: "timestamp with time zone",
                nullable: false,
                comment: "When it was sent.",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "user_agent",
                table: "support_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true,
                oldComment: "The free-form browser User-Agent, truncated. Diagnostic only.");

            migrationBuilder.AlterColumn<string>(
                name: "send_error",
                table: "support_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "The last SMTP failure reason, truncated. NULL when delivery succeeded or was neverattempted.");

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "support_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldComment: "What they wrote.");

            migrationBuilder.AlterColumn<string>(
                name: "ip_address",
                table: "support_messages",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true,
                oldComment: "The client IP as seen by the API (after forwarded-header processing). Used to spotabuse; never shown anywhere.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "email_sent_at_utc",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "NULL until the message was successfully handed to the SMTP server. NULL is normal whenSupportMail is not configured (e.g. dev).");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "support_messages",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254,
                oldComment: "The reply address of the person who wrote in (validated at the edge).");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "support_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When they wrote in.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "notifications",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The account this notification is for.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()",
                oldComment: "When it last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "title",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldComment: "The notification's headline.");

            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldComment: "The notification's body.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_read",
                table: "notifications",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Whether the customer has read it.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was created.");

            migrationBuilder.AlterColumn<string>(
                name: "verdict",
                table: "feedback_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true,
                oldComment: "NULL until the classifier has run. NULL means 'not classified', NOT 'junk' - the two aredifferent facts and the filter has to tell them apart.");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "feedback_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "The FuelFlow account this chat was bound to, or NULL for an unbound chat.");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "feedback_messages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldComment: "What the customer wrote, capped at the configured maximum length.");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_message_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "The id of this message inside that chat. Unique per chat - the webhook de-dupe key.");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_chat_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldComment: "Telegram chat id of the customer's private chat (positive for a 1-on-1 chat).");

            migrationBuilder.AlterColumn<string>(
                name: "summary",
                table: "feedback_messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true,
                oldComment: "The one-sentence restatement the classifier wrote for the staff chat. Never invented.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "feedback_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "Where the message stands.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "staff_replied_at_utc",
                table: "feedback_messages",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Set once an allowlisted team member's reply was delivered to the customer.");

            migrationBuilder.AlterColumn<long>(
                name: "staff_message_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "The id of the staff reply that was relayed.");

            migrationBuilder.AlterColumn<long>(
                name: "staff_chat_id",
                table: "feedback_messages",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Where this message was posted for the team, and the id it got there under. A staff replyis only relayed when it quotes this id, which is what stops group chatter reachingcustomers.");

            migrationBuilder.AlterColumn<string>(
                name: "severity",
                table: "feedback_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true,
                oldComment: "How serious the classifier says it is. NULL until it has run.");

            migrationBuilder.AlterColumn<string>(
                name: "platform",
                table: "feedback_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "The client platform (ios / android / web).");

            migrationBuilder.AlterColumn<string>(
                name: "language",
                table: "feedback_messages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true,
                oldComment: "The language the classifier detected. NULL until it has run.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "feedback_messages",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When the customer wrote it.");

            migrationBuilder.AlterColumn<decimal>(
                name: "confidence",
                table: "feedback_messages",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true,
                oldComment: "The classifier's confidence, 0..1. Retained even for dropped messages, so a driftingclassifier is visible in the admin filter rather than invisible in the discard bin. Adecimal, not a double: binary floating point cannot represent 0.1 exactly, and aconfidence of 0.6 is a number people read.");

            migrationBuilder.AlterColumn<string>(
                name: "classifier_error",
                table: "feedback_messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true,
                oldComment: "The last classifier failure, truncated. A row with this set and no verdict is one theteam can still read - it is only the automation that failed.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "classified_at_utc",
                table: "feedback_messages",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the classifier last ran, so a failed row can be retried or aged out.");

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "feedback_messages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldComment: "What the classifier says this is about. NULL until it has run.");

            migrationBuilder.AlterColumn<string>(
                name: "app_version",
                table: "feedback_messages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "The client build the message came from - the first thing asked for in a bug report.");

            migrationBuilder.AlterColumn<Guid>(
                name: "worker_user_id",
                table: "company_members",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The person.");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "company_members",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The company.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "joined_at_utc",
                table: "company_members",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When they joined.");

            migrationBuilder.AlterColumn<bool>(
                name: "is_deleted",
                table: "company_members",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldComment: "Firing a worker sets this rather than removing the row. Employment history IS history:the owner policy keeps anything that affects it forever, and 'this person worked hereuntil March' is a fact about the company, not garbage a cleanup job may collect. The!IsDeleted query filter keeps it out of every membership read.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "deleted_at_utc",
                table: "company_members",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "When the membership was soft-deleted, or NULL while it stands.");

            migrationBuilder.AlterColumn<Guid>(
                name: "worker_user_id",
                table: "company_invitations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The account the invitation names, once it is accepted.");

            migrationBuilder.AlterColumn<string>(
                name: "worker_phone_number",
                table: "company_invitations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "The phone number the invitation was sent to.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at_utc",
                table: "company_invitations",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it last changed.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "company_invitations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldComment: "Where the invitation stands.");

            migrationBuilder.AlterColumn<Guid>(
                name: "owner_user_id",
                table: "company_invitations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Who sent the invitation.");

            migrationBuilder.AlterColumn<Guid>(
                name: "legal_entity_id",
                table: "company_invitations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "The company that invited.");

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "company_invitations",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldComment: "When it was sent.");
        }
    }
}
