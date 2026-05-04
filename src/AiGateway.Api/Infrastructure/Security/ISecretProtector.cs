namespace AiGateway.Api.Infrastructure.Security;

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string encryptedText);
}
