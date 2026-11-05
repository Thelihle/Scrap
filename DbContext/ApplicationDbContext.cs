using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SCP_Control.Models;
using static SCP_Control.Controllers.ScrapApprovalController;

namespace SCP_Control.DbContext
{
    public class ApplicationDbContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        private readonly ILogger<ApplicationDbContext> _logger;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ILogger<ApplicationDbContext> logger)
            : base(options)
        {
            _logger = logger;
        }

        public DbSet<User> User { get; set; } = null!;
        public DbSet<UserRegistrationRequest> scpRequest { get; set; } = null!;
        public DbSet<Department> scpDepartment { get; set; } = null!;
        public DbSet<Line> scpLine { get; set; } = null!;
        public DbSet<ScrapSection> scpScrapSection { get; set; } = null!;
        public DbSet<ScrapError> scpScrapError { get; set; } = null!;
        public DbSet<ScrapEntry> ScrapEntries { get; set; } = null!;
        public DbSet<ScrapData> ScrapData { get; set; } = null!;
        public DbSet<TestLog> TestLogs { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; } = null!;
        public DbSet<ErrorLog> ErrorLogs { get; set; } = null!;
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<WeightHistory> WeightHistory { get; set; } = null!;
        public DbSet<StockMaster> StockMaster { get; set; }
        public DbSet<MaterialCostLookup> MaterialCostLookup { get; set; }
        public DbSet<UserSession> scpUserSession { get; set; }
        public DbSet<ScrapReviewSession> ScrapReviewSessions { get; set; } = null!;
        public DbSet<ScrapReviewEntry> ScrapReviewEntries { get; set; } = null!;
        public DbSet<RegisteredDevice> RegisteredDevices { get; set; }
        public DbSet<DeviceSession> DeviceSessions { get; set; }
        public DbSet<DeviceToken> DeviceTokens { get; set; }
        public DbSet<ScannerDevice> ScannerDevices { get; set; }
        public DbSet<ScannerAccessLog> ScannerAccessLogs { get; set; }
        public DbSet<BomComponent> BomComponents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Scanner Device Configuration
            modelBuilder.Entity<ScannerDevice>(entity =>
            {
                entity.ToTable("scpScannerDevices");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.IPAddress).IsUnique().HasDatabaseName("IX_scpScannerDevices_IPAddress");
                entity.Property(e => e.IPAddress).IsRequired().HasMaxLength(45);
                entity.Property(e => e.DeviceName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DefaultRole).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Description).HasMaxLength(200);
                entity.Property(e => e.Location).HasMaxLength(50);
                entity.Property(e => e.Department).HasMaxLength(50);
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(50);
                entity.Property(e => e.LastAccessUser).HasMaxLength(50);
                entity.Property(e => e.AllowQuickAccess).HasDefaultValue(true);
                entity.Property(e => e.AllowAnyRole).HasDefaultValue(true);
                entity.Property(e => e.IsActive).HasDefaultValue(true);
            });

            modelBuilder.Entity<ScannerAccessLog>(entity =>
            {
                entity.ToTable("scpScannerAccessLogs");
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.ScannerDevice)
                      .WithMany(e => e.AccessLogs)
                      .HasForeignKey(e => e.ScannerDeviceId)
                      .HasConstraintName("FK_scpScannerAccessLogs_scpScannerDevices");
                entity.HasIndex(e => e.AccessDateTime).HasDatabaseName("IX_scpScannerAccessLogs_AccessDateTime");
                entity.HasIndex(e => e.ScannerDeviceId).HasDatabaseName("IX_scpScannerAccessLogs_ScannerDeviceId");
                entity.HasIndex(e => e.UserClockNumber).HasDatabaseName("IX_scpScannerAccessLogs_UserClockNumber");
                entity.Property(e => e.AccessPath).HasMaxLength(200);
                entity.Property(e => e.UserAgent).HasMaxLength(500);
                entity.Property(e => e.UserClockNumber).HasMaxLength(50);
                entity.Property(e => e.UserRole).HasMaxLength(50);
                entity.Property(e => e.AccessType).HasMaxLength(50);
                entity.Property(e => e.Notes).HasMaxLength(1000);
            });

            modelBuilder.Entity<IdentityUserToken<int>>(entity =>
            {
                entity.ToTable("AspNetUserTokens");
                entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });
                entity.Property(e => e.LoginProvider).HasMaxLength(128);
                entity.Property(e => e.Name).HasMaxLength(128);
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(ut => ut.UserId)
                    .IsRequired();
            });

            modelBuilder.Entity<IdentityUserClaim<int>>(entity =>
            {
                entity.ToTable("AspNetUserClaims");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ClaimType).HasMaxLength(255);
                entity.Property(e => e.ClaimValue).HasMaxLength(255);
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(uc => uc.UserId)
                    .IsRequired();
            });

            modelBuilder.Entity<IdentityRoleClaim<int>>(entity =>
            {
                entity.ToTable("AspNetRoleClaims");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ClaimType).HasMaxLength(255);
                entity.Property(e => e.ClaimValue).HasMaxLength(255);
                entity.HasOne<IdentityRole<int>>()
                    .WithMany()
                    .HasForeignKey(rc => rc.RoleId)
                    .IsRequired();
            });

            modelBuilder.Entity<WeightHistory>(entity =>
            {
                entity.ToTable("scpWeightHistory");
                entity.HasKey(e => e.WeightId);
                entity.Property(e => e.Department).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Line).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Shift).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Weight).HasColumnType("decimal(18, 3)").IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()").IsRequired();
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ModifiedAt).HasDefaultValueSql("GETDATE()").IsRequired();
                entity.Property(e => e.ModifiedBy).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Description).HasMaxLength(500).IsRequired(false);
                entity.Property(e => e.SecurityStamp).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.IsBulkWeighing).HasColumnType("bit").HasDefaultValue(false).IsRequired();
                entity.HasIndex(e => new { e.Department, e.Line, e.Shift, e.CreatedAt }).HasDatabaseName("IX_WeightHistory_Location_Date");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_WeightHistory_CreatedAt");
                entity.HasIndex(e => e.SecurityStamp).HasDatabaseName("IX_WeightHistory_SecurityStamp");
                entity.HasIndex(e => e.IsBulkWeighing).HasDatabaseName("IX_WeightHistory_IsBulkWeighing");
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("scpUser");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("UserId");
                entity.Property(e => e.ClockNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.UserName).IsRequired().HasMaxLength(256);
                entity.Property(e => e.UserType).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.Department).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.Line).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.Shift).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired(false);
                entity.Property(e => e.Email).HasMaxLength(256).IsRequired(false);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()").IsRequired();
                entity.Property(e => e.CreatedBy).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.LastModifiedAt).IsRequired(false);
                entity.Property(e => e.LastModifiedBy).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
                entity.Property(e => e.PasswordResetRequired).HasDefaultValue(false).IsRequired(false);
                entity.Property(e => e.LastPasswordChange).IsRequired(false);
                entity.Property(e => e.LockoutEnabled).HasDefaultValue(true).IsRequired();
                entity.Property(e => e.AccessFailedCount).HasDefaultValue(0).IsRequired();
                entity.Property(e => e.LockoutEnd).IsRequired(false);
                entity.Ignore(e => e.Password);
                entity.Ignore(e => e.TempPassword);
                entity.Ignore(e => e.IsTemporary);
                entity.Ignore(e => e.NormalizedUserName);
                entity.Ignore(e => e.NormalizedEmail);
                entity.Ignore(e => e.EmailConfirmed);
                entity.Ignore(e => e.SecurityStamp);
                entity.Ignore(e => e.ConcurrencyStamp);
                entity.Ignore(e => e.PhoneNumber);
                entity.Ignore(e => e.PhoneNumberConfirmed);
                entity.Ignore(e => e.TwoFactorEnabled);
                entity.HasIndex(e => e.ClockNumber).IsUnique().HasDatabaseName("IX_scpUser_ClockNumber");
                entity.HasIndex(e => e.UserName).HasDatabaseName("IX_scpUser_UserName");
            });

            modelBuilder.Entity<IdentityUserLogin<int>>(entity =>
            {
                entity.ToTable("AspNetUserLogins");
                entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });
                entity.Property(e => e.LoginProvider).HasMaxLength(128);
                entity.Property(e => e.ProviderKey).HasMaxLength(128);
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(ul => ul.UserId)
                    .IsRequired();
            });

            modelBuilder.Entity<UserSession>(entity =>
            {
                entity.ToTable("scpUserSession");
                entity.HasKey(e => e.SecurityStamp);
                entity.Property(e => e.ClockNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.SecurityStamp).HasMaxLength(100);
                entity.Property(e => e.IpAddress).HasMaxLength(50);
                entity.Property(e => e.UserAgent).HasMaxLength(1000);
            });

            modelBuilder.Entity<MaterialCostLookup>(entity =>
            {
                entity.HasNoKey();
                entity.ToView("MaterialCostLookup");
            });

            modelBuilder.Entity<StockMaster>(entity =>
            {
                entity.ToTable("InvMaster", "dbo", t => t.ExcludeFromMigrations());
                entity.HasKey(e => e.StockCode);
                entity.Property(e => e.MaterialCost).HasColumnType("decimal(18,2)");
            });

            modelBuilder.Entity<IdentityRole<int>>(entity =>
            {
                entity.ToTable("AspNetRoles");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).HasMaxLength(256);
                entity.Property(e => e.NormalizedName).HasMaxLength(256);
                entity.HasIndex(e => e.NormalizedName)
                    .HasDatabaseName("RoleNameIndex")
                    .IsUnique()
                    .HasFilter("[NormalizedName] IS NOT NULL");
            });

            modelBuilder.Entity<IdentityUserRole<int>>(entity =>
            {
                entity.ToTable("AspNetUserRoles");
                entity.HasKey(e => new { e.UserId, e.RoleId });
                entity.HasOne<User>().WithMany().HasForeignKey(ur => ur.UserId).IsRequired();
                entity.HasOne<IdentityRole<int>>().WithMany().HasForeignKey(ur => ur.RoleId).IsRequired();
            });

            modelBuilder.Entity<LoginAttempt>(entity =>
            {
                entity.ToTable("scpLoginAttempts", "dbo");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("Id");
                entity.Property(e => e.Identifier).HasColumnName("Identifier").IsRequired().HasMaxLength(50);
                entity.Property(e => e.IpAddress).HasColumnName("IpAddress").HasMaxLength(45);
                entity.Property(e => e.AttemptTime).HasColumnName("AttemptTime");
                entity.Property(e => e.IsSuccessful).HasColumnName("IsSuccessful");
                entity.HasIndex(e => new { e.Identifier, e.AttemptTime }).HasDatabaseName("IX_LoginAttempts_Identifier_AttemptTime");
            });

            modelBuilder.Entity<UserRegistrationRequest>(entity =>
            {
                entity.HasKey(e => e.RequestId);
                entity.Property(e => e.ClockNumber).HasMaxLength(50);
                entity.Property(e => e.Name).HasMaxLength(100);
                entity.Property(e => e.Surname).HasMaxLength(100);
                entity.Property(e => e.Email).HasMaxLength(50);
                entity.Property(e => e.UserType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Shift).IsRequired().HasMaxLength(10);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50).HasDefaultValue("Pending");
                entity.Property(e => e.RequestDate).HasDefaultValueSql("GETDATE()");
                entity.Property(e => e.ProcessedBy).HasMaxLength(50);
                entity.Property(e => e.LineId).IsRequired(false);
                entity.HasOne(u => u.Department).WithMany().HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(u => u.Line).WithMany().HasForeignKey(u => u.LineId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
            });

            modelBuilder.Entity<ScrapSection>(entity =>
            {
                entity.ToTable("scpScrapSection");
                entity.HasKey(e => e.ScrapSectionId);
                entity.Property(e => e.ScrapSectionName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.IsActive).HasColumnType("bit").IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()").IsRequired();
                entity.Property(e => e.ModifiedAt).IsRequired(false);
                entity.Property(e => e.RequiresPosition).HasDefaultValue(false);
                entity.HasMany(s => s.Errors).WithOne(e => e.Section).HasForeignKey(e => e.ScrapSectionId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasKey(e => e.DepartmentId);
                entity.ToTable("scpDepartment", schema: "dbo");
                entity.Property(e => e.DepartmentName).HasColumnType("nvarchar(200)").IsRequired();
                entity.Property(e => e.IsActive).HasColumnType("bit").IsRequired();
                entity.Property(e => e.Location).HasColumnType("nvarchar(max)").IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnType("datetime2").IsRequired();
                entity.Property(e => e.ModifiedAt).HasColumnType("datetime2").IsRequired(false);
            });

            modelBuilder.Entity<ScrapError>(entity =>
            {
                entity.ToTable("scpReason");
                entity.HasKey(e => e.ErrorId);
                entity.Property(e => e.ErrorId).ValueGeneratedOnAdd();
                entity.Property(e => e.ErrorName).HasColumnName("ScrapReasonName").IsRequired().HasMaxLength(100);
                entity.Property(e => e.RequiresPosition).HasColumnName("Part_Position").HasDefaultValue(false);
                entity.Property(e => e.DefectItem).HasColumnName("Defect_Item").HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.PartDescription).HasColumnName("Part_Description").HasMaxLength(200).IsRequired(false);
                entity.Property(e => e.IsActive).HasColumnType("bit").IsRequired();
                entity.Property(e => e.RequiresPosition).HasColumnType("bit").IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()").IsRequired();
                entity.Property(e => e.ModifiedAt).IsRequired(false);
                entity.HasOne(e => e.Section).WithMany(s => s.Errors).HasForeignKey(e => e.ScrapSectionId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ScrapEntry>(entity =>
            {
                entity.ToTable("ScrapLog");
                entity.HasKey(e => e.ReasonId);
                entity.HasOne(e => e.ScrapSection).WithMany().HasForeignKey(e => e.ScrapSectionId).OnDelete(DeleteBehavior.Restrict);
                entity.Property(e => e.PartNumber).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Position).HasMaxLength(50);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(50);
            });

            modelBuilder.Entity<ScrapData>(entity =>
            {
                entity.ToTable("scpScrapData", "dbo");
                entity.HasKey(e => e.ScrapId);

                // Core nullable string fields
                entity.Property(e => e.ClockNumber).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.PartNumber).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ScrapReason).HasMaxLength(255).IsRequired(false);
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.Department).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.Line).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.Shift).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.Position).HasMaxLength(100).IsRequired(false);
                entity.Property(e => e.StockUom).HasMaxLength(10).IsRequired(false);
                entity.Property(e => e.ScrapSectionId).IsRequired(false);
                entity.Property(e => e.MaterialCost).HasColumnType("decimal(18,5)").IsRequired(false);
                entity.Property(e => e.CreatedBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ModifiedBy).HasMaxLength(50).IsRequired(false);

                // Required core fields
                entity.Property(e => e.Quantity).IsRequired();
                entity.Property(e => e.SubmissionDate).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ModifiedAt).IsRequired();

                // Approval fields
                entity.Property(e => e.ApprovedBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ApprovalReason).HasMaxLength(500).IsRequired(false);
                entity.Property(e => e.RejectedBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.RejectedReason).HasMaxLength(500).IsRequired(false);
                entity.Property(e => e.TeamLeaderApprovalStatus).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.TeamLeaderApprovalBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.GroupLeaderApprovalStatus).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.GroupLeaderApprovalBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.ManagerApprovalStatus).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.ManagerApprovalBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.SeniorManagerApprovalStatus).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.SeniorManagerApprovalBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.CurrentApprovalLevel).IsRequired(false);
                entity.Property(e => e.FirstApprovalStatus).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.FirstApprovalBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.FirstApprovalReason).HasMaxLength(500).IsRequired(false);

                // Weight fields
                entity.Property(e => e.ActualWeight).HasColumnType("decimal(10,3)").IsRequired(false);
                entity.Property(e => e.WeightRecordedBy).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.WeightNotRecordedReason).HasMaxLength(255).IsRequired(false);

                entity.Ignore(e => e.RejectedDate);

                // Machine fields
                entity.Property(e => e.MachineType).HasMaxLength(50).IsRequired(false);
                entity.Property(e => e.MachineNumber).HasMaxLength(10).IsRequired(false);

                // Scrap type / weight
                entity.Property(e => e.ScrapType).HasMaxLength(20).IsRequired(false);
                entity.Property(e => e.Weight).HasColumnType("decimal(18, 3)").IsRequired(false);
                entity.Property(e => e.IsWeighed).IsRequired(false).HasDefaultValue(false);

                // Inventory / plant fields
                entity.Property(e => e.PartCategory).HasMaxLength(1).IsUnicode(true);
                entity.Property(e => e.Plant).HasMaxLength(50).IsUnicode(true);
                entity.Property(e => e.Company).HasMaxLength(1).IsUnicode(true).IsRequired(false).HasDefaultValue("B");

                // ✅ NEW: Engineering plant field
                entity.Property(e => e.EngineeringPlant)
                      .HasMaxLength(50)
                      .IsUnicode(true)
                      .IsRequired(false);

                // Batch / processed fields
                entity.Property(e => e.BatchScrapId).HasColumnType("int");
                entity.Property(e => e.ProcessedData).HasMaxLength(500).IsRequired(false);

                entity.HasIndex(e => e.BatchScrapId)
                      .HasDatabaseName("IX_scpScrapData_BatchScrapId")
                      .HasFilter("[BatchScrapId] IS NOT NULL");

                entity.HasIndex(e => new { e.ClockNumber, e.BatchScrapId })
                      .HasDatabaseName("IX_scpScrapData_ClockNumber_BatchScrapId")
                      .HasFilter("[BatchScrapId] IS NOT NULL");
            });

            modelBuilder.Entity<ActivityLog>(entity =>
            {
                entity.ToTable("scpActivityLogs", "dbo");
                entity.HasKey(e => e.LogId);
                entity.Property(e => e.LogId).HasColumnName("LogId");
                entity.Property(e => e.UserId).HasColumnName("UserId");
                entity.Property(e => e.ClockNumber).HasColumnName("ClockNumber").HasMaxLength(50);
                entity.Property(e => e.ActionType).HasColumnName("ActionType").HasMaxLength(50);
                entity.Property(e => e.ActionDetails).HasColumnName("ActionDetails").HasMaxLength(500);
                entity.Property(e => e.Status).HasColumnName("Status").HasMaxLength(50);
                entity.Property(e => e.IPAddress).HasColumnName("IPAddress").HasMaxLength(45);
                entity.Property(e => e.UserAgent).HasColumnName("UserAgent").HasMaxLength(500);
                entity.Property(e => e.SecurityStamp).HasColumnName("SecurityStamp").HasMaxLength(100);
                entity.Property(e => e.DepartmentName).HasColumnName("DepartmentName").HasMaxLength(100);
                entity.Property(e => e.LineName).HasColumnName("LineName").HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<ErrorLog>(entity =>
            {
                entity.ToTable("scpErrorLogs", "dbo");
                entity.HasKey(e => e.ErrorId);
                entity.Property(e => e.ErrorId).HasColumnName("ErrorId");
                entity.Property(e => e.UserId).HasColumnName("UserId");
                entity.Property(e => e.ClockNumber).HasColumnName("ClockNumber").HasMaxLength(50);
                entity.Property(e => e.ErrorMessage).HasColumnName("ErrorMessage").HasMaxLength(4000).IsRequired();
                entity.Property(e => e.StackTrace).HasColumnName("StackTrace").HasMaxLength(4000);
                entity.Property(e => e.ErrorSource).HasColumnName("ErrorSource").HasMaxLength(255);
                entity.Property(e => e.IPAddress).HasColumnName("IPAddress").HasMaxLength(50);
                entity.Property(e => e.UserAgent).HasColumnName("UserAgent").HasMaxLength(500);
                entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.ClockNumber);
            });

            modelBuilder.Entity<ScrapReviewSession>(entity =>
            {
                entity.ToTable("scpScrapReviewSessions");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SecurityStamp).IsUnique();
                entity.HasIndex(e => e.ClockNumber);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => new { e.ClockNumber, e.IsActive });
            });

            modelBuilder.Entity<ScrapReviewEntry>(entity =>
            {
                entity.ToTable("scpScrapReviewEntries");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SecurityStamp);
                entity.HasIndex(e => new { e.SecurityStamp, e.OrderIndex });
                entity.HasOne(e => e.Session)
                      .WithMany(s => s.Entries)
                      .HasForeignKey(e => e.SecurityStamp)
                      .HasPrincipalKey(s => s.SecurityStamp)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BomComponent>(entity =>
            {
                entity.ToTable("scpBomComponents");
                entity.HasKey(e => e.BomComponentId);
                entity.HasOne(e => e.ScrapData).WithMany().HasForeignKey(e => e.ScrapId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(e => e.ScrapId);
                entity.HasIndex(e => e.ParentPart);
            });

            modelBuilder.Entity<RegisteredDevice>(entity =>
            {
                entity.HasKey(e => e.DeviceId);
                entity.HasIndex(e => e.MacAddress).IsUnique();
                entity.HasIndex(e => e.DeviceFingerprint).IsUnique();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETDATE()");
                entity.Property(e => e.LastSeen).HasDefaultValueSql("GETDATE()");
            });

            modelBuilder.Entity<DeviceSession>(entity =>
            {
                entity.HasKey(e => e.SecurityStamp);
                entity.HasOne(d => d.Device).WithMany(p => p.DeviceSessions).HasForeignKey(d => d.DeviceId).OnDelete(DeleteBehavior.Cascade);
                entity.Property(e => e.LoginTime).HasDefaultValueSql("GETDATE()");
                entity.Property(e => e.LastActivity).HasDefaultValueSql("GETDATE()");
            });

            modelBuilder.Entity<DeviceToken>(entity =>
            {
                entity.HasKey(e => e.TokenId);
                entity.HasIndex(e => e.Token).IsUnique();
                entity.HasOne(d => d.Device).WithMany(p => p.DeviceTokens).HasForeignKey(d => d.DeviceId).OnDelete(DeleteBehavior.Cascade);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETDATE()");
            });
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
                {
                    switch (sqlEx.Number)
                    {
                        case 2714:
                            _logger.LogWarning("Attempted to create an object that already exists: {Message}", sqlEx.Message);
                            return 0;
                        case 547:
                            _logger.LogError("Foreign key constraint violation: {Message}", sqlEx.Message);
                            throw new DbUpdateException("A related record does not exist.", ex);
                        case 2601:
                        case 2627:
                            _logger.LogError("Unique constraint violation: {Message}", sqlEx.Message);
                            throw new DbUpdateException("A record with the same key already exists.", ex);
                        default:
                            _logger.LogError(ex, "An unexpected SQL error occurred: {Message}", sqlEx.Message);
                            throw;
                    }
                }
                _logger.LogError(ex, "A database error occurred that was not a SQL exception");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while saving changes");
                throw;
            }
        }
    }
}