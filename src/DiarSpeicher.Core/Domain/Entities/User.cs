namespace DiarSpeicher.Core.Domain.Entities;

public class User
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string Username { get; set; } = null!;
    public bool IsServerOwner { get; set; }

    public string? AvatarPath { get; set; }
    public string? AvatarMeta { get; set; }
    public DateTimeOffset? AvatarUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public UserPreferences? Preferences { get; set; }
    public AgeRestriction? AgeRestriction { get; set; }
    public ICollection<LibraryExclusion> ExcludedLibraries { get; set; } = new List<LibraryExclusion>();
    public ICollection<ReadingSession> ReadingSessions { get; set; } = new List<ReadingSession>();
}

public class UserPreferences
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public User User { get; set; } = null!;

    public string Locale { get; set; } = "es";
    public string AppTheme { get; set; } = "dark";
    public string AppFont { get; set; } = "inter";
    public bool EnableCompactDisplay { get; set; }
}

public class AgeRestriction
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public User User { get; set; } = null!;

    public int Age { get; set; }
    public bool RestrictOnUnset { get; set; } = true;
}
