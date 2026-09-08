namespace DiarSpeicher.Core.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string? hashedPassword);
}
