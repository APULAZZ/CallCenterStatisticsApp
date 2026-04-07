using CallCenterStatisticsApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;

namespace CallCenterStatisticsApp.Data;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<CallGroup> CallGroups => Set<CallGroup>();
    public DbSet<CallTopic> CallTopics => Set<CallTopic>();
    public DbSet<EmployeeGroup> EmployeeGroups => Set<EmployeeGroup>();
    public DbSet<CallRecord> CallRecords => Set<CallRecord>();
    public DbSet<MangoSyncLog> MangoSyncLogs => Set<MangoSyncLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<CallStatusRule> CallStatusRules => Set<CallStatusRule>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Extension).HasMaxLength(50);
            entity.Property(x => x.MangoUserId).HasMaxLength(100);
            entity.Property(x => x.MangoUserKey).HasMaxLength(100);

            entity.HasIndex(x => x.MangoUserId);
        });

        modelBuilder.Entity<CallGroup>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.MangoGroupId).HasMaxLength(100);

            entity.HasIndex(x => x.MangoGroupId);
        });

        modelBuilder.Entity<CallTopic>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.MangoTopicId).HasMaxLength(100);

            entity.HasIndex(x => x.MangoTopicId);
        });

        modelBuilder.Entity<EmployeeGroup>(entity =>
        {
            entity.HasOne(x => x.Employee)
                .WithMany(x => x.EmployeeGroups)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Group)
                .WithMany(x => x.EmployeeGroups)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CallRecord>(entity =>
        {
            entity.Property(x => x.MangoCallId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExternalPhoneNumber).HasMaxLength(50);
            entity.Property(x => x.Direction).HasMaxLength(50).IsRequired();
            entity.Property(x => x.StatusCode).HasMaxLength(100);
            entity.Property(x => x.StatusText).HasMaxLength(200);

            entity.HasIndex(x => x.MangoCallId).IsUnique();
            entity.HasIndex(x => x.CallDateTime);

            entity.HasOne(x => x.Employee)
                .WithMany(x => x.CallRecords)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Group)
                .WithMany(x => x.CallRecords)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Topic)
                .WithMany(x => x.CallRecords)
                .HasForeignKey(x => x.TopicId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<CallStatusRule>(entity =>
        {
            entity.Property(x => x.StatusCode).HasMaxLength(100).IsRequired();
            entity.Property(x => x.StatusText).HasMaxLength(200);
            entity.HasIndex(x => x.StatusCode);
        });

        base.OnModelCreating(modelBuilder);
    }
}