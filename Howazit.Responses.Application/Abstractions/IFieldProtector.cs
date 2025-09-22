namespace Howazit.Responses.Application.Abstractions;

public interface IFieldProtector {
    string? Protect(string? plaintext);
    string? Unprotect(string? ciphertext);
}