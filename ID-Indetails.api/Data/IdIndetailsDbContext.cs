using ID_Indetails.api.Enums;
using ID_Indetails.api.Models;
using Microsoft.EntityFrameworkCore;

namespace ID_Indetails.api.Data;

public class IdIndetailsDbContext(DbContextOptions<IdIndetailsDbContext> options) : DbContext(options)
{
    // Identity and access
    public DbSet<Domain> Domains => Set<Domain>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Discipline> Disciplines => Set<Discipline>();
    public DbSet<UserPayHistory> UserPayHistory => Set<UserPayHistory>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    // Clients and projects
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectRequirement> ProjectRequirements => Set<ProjectRequirement>();
    public DbSet<RequirementAssignee> RequirementAssignees => Set<RequirementAssignee>();
    public DbSet<ProjectMeeting> ProjectMeetings => Set<ProjectMeeting>();
    public DbSet<ProjectExpense> ProjectExpenses => Set<ProjectExpense>();

    // Design work tracking
    public DbSet<WorkEntry> WorkEntries => Set<WorkEntry>();
    public DbSet<WorkEntryComment> WorkEntryComments => Set<WorkEntryComment>();

    // Execution site costs
    public DbSet<ProjectMaterial> ProjectMaterials => Set<ProjectMaterial>();
    public DbSet<LabourWeek> LabourWeeks => Set<LabourWeek>();
    public DbSet<LabourWeekDay> LabourWeekDays => Set<LabourWeekDay>();
    public DbSet<ProjectLabourDefault> ProjectLabourDefaults => Set<ProjectLabourDefault>();
    public DbSet<ClientStatement> ClientStatements => Set<ClientStatement>();
    public DbSet<ClientStatementLine> ClientStatementLines => Set<ClientStatementLine>();
    public DbSet<ClientStatementDelivery> ClientStatementDeliveries => Set<ClientStatementDelivery>();
    public DbSet<StatementSequence> StatementSequences => Set<StatementSequence>();

    // Working time and the hours ledger
    public DbSet<WorkingTimePolicy> WorkingTimePolicies => Set<WorkingTimePolicy>();
    public DbSet<UserSchedule> UserSchedules => Set<UserSchedule>();
    public DbSet<UserScheduleDay> UserScheduleDays => Set<UserScheduleDay>();
    public DbSet<HourExcuse> HourExcuses => Set<HourExcuse>();
    public DbSet<HourExcuseDay> HourExcuseDays => Set<HourExcuseDay>();

    // Billing
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<InvoiceDelivery> InvoiceDeliveries => Set<InvoiceDelivery>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // Company layer
    public DbSet<OverheadItem> OverheadItems => Set<OverheadItem>();
    public DbSet<OverheadAmount> OverheadAmounts => Set<OverheadAmount>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<DomainSettings> DomainSettings => Set<DomainSettings>();

    // Activity, messaging and files
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageMention> MessageMentions => Set<MessageMention>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<ConversationRead> ConversationReads => Set<ConversationRead>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<UserBoardLayout> UserBoardLayouts => Set<UserBoardLayout>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdIndetailsDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money: DECIMAL(18,2) unless a configuration says otherwise (rates, quantities).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Timestamps: DATETIME(3), always UTC.
        configurationBuilder.Properties<DateTime>()
            .HavePrecision(3)
            .HaveConversion<UtcDateTimeConverter>();

        // Strings: VARCHAR(255) by default so every text column is indexable; long text
        // columns opt into TEXT explicitly.
        configurationBuilder.Properties<string>().HaveMaxLength(255);

        // Enums: readable snake_case strings, limited by a CHECK constraint per column.
        StoreAsSnakeCase<UserStatus>(configurationBuilder);
        StoreAsSnakeCase<ProjectStatus>(configurationBuilder);
        StoreAsSnakeCase<BillingType>(configurationBuilder);
        StoreAsSnakeCase<PayBasis>(configurationBuilder);
        StoreAsSnakeCase<ContactKind>(configurationBuilder);
        StoreAsSnakeCase<DeliveryChannel>(configurationBuilder);
        StoreAsSnakeCase<PaymentMethod>(configurationBuilder);
        StoreAsSnakeCase<OverheadCostGroup>(configurationBuilder);
        StoreAsSnakeCase<ConversationType>(configurationBuilder);
        StoreAsSnakeCase<EditWindow>(configurationBuilder);
        StoreAsSnakeCase<EntryNotificationMode>(configurationBuilder);
        StoreAsSnakeCase<ExcuseKind>(configurationBuilder);
        StoreAsSnakeCase<StatementMode>(configurationBuilder);
        StoreAsSnakeCase<PermissionEffect>(configurationBuilder);
    }

    private static void StoreAsSnakeCase<TEnum>(ModelConfigurationBuilder configurationBuilder)
        where TEnum : struct, Enum
        => configurationBuilder.Properties<TEnum>()
            .HaveConversion<SnakeCaseEnumConverter<TEnum>>()
            .HaveMaxLength(20);
}
