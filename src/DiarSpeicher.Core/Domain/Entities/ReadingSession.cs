using DiarSpeicher.Core.Domain.Enums;

namespace DiarSpeicher.Core.Domain.Entities;

public class ReadingSession
{
    public int Id { get; set; }

    public DateOnly SessionDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public int? StartPage { get; set; }
    public int? EndPage { get; set; }

    public decimal? StartPercentage { get; set; }
    public decimal? EndPercentage { get; set; }

    public string? KoreaderProgress { get; set; }
    public long? ElapsedSeconds { get; set; }

    public int ReadthroughNumber { get; set; } = 1;
    public ReadingStatus Status { get; set; } = ReadingStatus.Reading;

    public string? Notes { get; set; }

    public string MediaId { get; set; } = null!;
    public Media Media { get; set; } = null!;

    public string UserId { get; set; } = null!;
    public User User { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsComplete() => Status == ReadingStatus.Finished;
    public bool IsFinalized() => Status is ReadingStatus.Finished or ReadingStatus.Abandoned;
}
