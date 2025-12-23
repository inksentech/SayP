using Microsoft.EntityFrameworkCore;
using SayP.Domain.Entities;

namespace SayP.Domain.Interfaces;

/// <summary>
/// SayP database context interface
/// </summary>
public interface ISayPDbContext
{
    DbSet<TenantMapping> TenantMappings { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<Command> Commands { get; }
    DbSet<DialogueState> DialogueStates { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<UserBehaviorLog> UserBehaviorLogs { get; }
    DbSet<TenantSettings> TenantSettings { get; }
    DbSet<TenantSettingsLog> TenantSettingsLogs { get; }
    
    // Self-Learning & AI Enhancement Tables
    DbSet<LearnedAlias> LearnedAliases { get; }
    DbSet<TenantTerminology> TenantTerminologies { get; }
    DbSet<ContextReference> ContextReferences { get; }
    DbSet<UserFeedback> UserFeedbacks { get; }
    DbSet<GlobalLearningEntry> GlobalLearningPool { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
