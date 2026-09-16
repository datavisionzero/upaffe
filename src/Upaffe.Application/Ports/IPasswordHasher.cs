namespace Upaffe.Application.Ports;

public interface IPasswordHasher
{
    Task<string> HashAsync(string password, CancellationToken cancellationToken);
    Task<bool> VerifyAsync(string encodedHash, string password, CancellationToken cancellationToken);
}
