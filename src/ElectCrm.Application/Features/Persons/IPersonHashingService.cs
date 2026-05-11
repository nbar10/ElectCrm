namespace ElectCrm.Application.Features.Persons;

public interface IPersonHashingService
{
    string HashValue(string normalisedValue);
    string EncryptValue(string plaintext);
    string DecryptValue(string ciphertext);
}
