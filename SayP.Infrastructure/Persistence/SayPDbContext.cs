using Microsoft.EntityFrameworkCore;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;

namespace SayP.Infrastructure.Persistence;

public class SayPDbContext : DbContext, ISayPDbContext
{
    public SayPDbContext(DbContextOptions<SayPDbContext> options) : base(options)
    {
    }

    public DbSet<TenantMapping> TenantMappings => Set<TenantMapping>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<DialogueState> DialogueStates => Set<DialogueState>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserBehaviorLog> UserBehaviorLogs => Set<UserBehaviorLog>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<TenantSettingsLog> TenantSettingsLogs => Set<TenantSettingsLog>();
    
    // Self-Learning & AI Enhancement Tables
    public DbSet<LearnedAlias> LearnedAliases => Set<LearnedAlias>();
    public DbSet<TenantTerminology> TenantTerminologies => Set<TenantTerminology>();
    public DbSet<ContextReference> ContextReferences => Set<ContextReference>();
    public DbSet<UserFeedback> UserFeedbacks => Set<UserFeedback>();
    public DbSet<GlobalLearningEntry> GlobalLearningPool => Set<GlobalLearningEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TenantMapping configuration
        modelBuilder.Entity<TenantMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PhoneNumber).IsUnique();
            entity.HasIndex(e => e.TenantId);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(20);
        });

        // Conversation configuration
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PhoneNumber, e.TenantId });
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.PhoneNumber).IsRequired().HasMaxLength(20);
            
            entity.HasMany(e => e.Messages)
                .WithOne(m => m.Conversation)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Commands)
                .WithOne(c => c.Conversation)
                .HasForeignKey(c => c.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.WhatsAppMessageId).IsUnique();
            entity.HasIndex(e => e.ConversationId);
            entity.Property(e => e.WhatsAppMessageId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.From).IsRequired().HasMaxLength(20);
            entity.Property(e => e.To).IsRequired().HasMaxLength(20);
        });

        // Command configuration
        modelBuilder.Entity<Command>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Type);
        });

        // DialogueState configuration
        modelBuilder.Entity<DialogueState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ConversationId, e.IsActive });
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.CollectedSlotsJson).IsRequired();
            entity.Property(e => e.MissingSlotsJson).IsRequired();
            
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // LearnedAlias configuration
        modelBuilder.Entity<LearnedAlias>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.Intent });
            entity.HasIndex(e => e.NormalizedAlias);
            entity.HasIndex(e => new { e.Language, e.IsActive });
            entity.Property(e => e.Intent).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Alias).IsRequired().HasMaxLength(500);
            entity.Property(e => e.NormalizedAlias).IsRequired().HasMaxLength(500);
        });

        // TenantTerminology configuration
        modelBuilder.Entity<TenantTerminology>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TenantId, e.StandardTerm });
            entity.HasIndex(e => new { e.TenantId, e.NormalizedCustomTerm });
            entity.Property(e => e.StandardTerm).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CustomTerm).IsRequired().HasMaxLength(100);
            entity.Property(e => e.NormalizedCustomTerm).IsRequired().HasMaxLength(100);
        });

        // ContextReference configuration
        modelBuilder.Entity<ContextReference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ConversationId, e.ReferenceType });
            entity.HasIndex(e => e.EntityId);
            entity.Property(e => e.ReferenceType).IsRequired().HasMaxLength(50);
            
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserFeedback configuration
        modelBuilder.Entity<UserFeedback>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserProfileId);
            entity.HasIndex(e => new { e.FeedbackType, e.IsProcessed });
            entity.HasIndex(e => e.DetectedIntent);
            entity.Property(e => e.FeedbackType).IsRequired().HasMaxLength(50);
            
            entity.HasOne(e => e.UserProfile)
                .WithMany()
                .HasForeignKey(e => e.UserProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GlobalLearningEntry configuration
        modelBuilder.Entity<GlobalLearningEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NormalizedPattern);
            entity.HasIndex(e => new { e.Intent, e.Language });
            entity.HasIndex(e => new { e.IsActive, e.GlobalConfidenceScore });
            entity.Property(e => e.Pattern).IsRequired().HasMaxLength(500);
            entity.Property(e => e.NormalizedPattern).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Intent).IsRequired().HasMaxLength(100);
        });
    }
}
