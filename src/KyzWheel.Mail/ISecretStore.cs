namespace KyzWheel.Mail;

public interface ISecretStore
{
    void Set(string key, string secret);
    string? Get(string key);
    void Remove(string key);
}
