namespace DiarSpeicher.Core.Domain.Entities;

public class Session
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string UserId { get; set; } = null!;
    public User User { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
