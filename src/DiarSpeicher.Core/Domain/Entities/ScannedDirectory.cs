namespace DiarSpeicher.Core.Domain.Entities;

public class ScannedDirectory
{
    public string Path { get; set; } = null!;
    public long LastMTime { get; set; }
}
