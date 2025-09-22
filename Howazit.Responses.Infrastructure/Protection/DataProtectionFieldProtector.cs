using Howazit.Responses.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Howazit.Responses.Infrastructure.Protection;

public class DataProtectionFieldProtector : IFieldProtector {
    private readonly IDataProtector _writer;
    private readonly List<IDataProtector> _readers;
    private readonly ILogger<DataProtectionFieldProtector> _log;


    public string? Protect(string? plaintext) {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        try {
            return _writer.Protect(plaintext);
        }
        catch (Exception ex) {
            Logs.FailedToProtect(_log, ex);
            return plaintext;
        }
    }

    public string? Unprotect(string? ciphertext) {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;

        foreach (var prot in _readers) {
            try {
                return prot.Unprotect(ciphertext);
            }
            catch { /* try next */
            }
        }

        Logs.UnableToUnprotect(_log);
        return ciphertext;
    }

    public sealed class Options {
        public string Purpose { get; set; } = "howazit:v1:pii";

        /// <summary>Comma-separated list of previous purposes (read-only, for key rotation).</summary>
        public string[] PreviousPurposes { get; set; } = [];

        /// <summary>Whether to encrypt UserAgent in addition to IpAddress.</summary>
        public bool EncryptUserAgent { get; set; }
    }


    public DataProtectionFieldProtector(
        IDataProtectionProvider provider,
        Options options,
        ILogger<DataProtectionFieldProtector> log) {
        _writer = provider.CreateProtector(options.Purpose);
        _readers = new List<IDataProtector> { _writer };

        foreach (var p in options.PreviousPurposes ?? Array.Empty<string>()) {
            if (!string.IsNullOrWhiteSpace(p))
                _readers.Add(provider.CreateProtector(p.Trim()));
        }

        _log = log;
    }
}

// -------- LoggerMessages --------
internal static partial class Logs {
    [LoggerMessage(LogLevel.Warning, "Failed to protect field; returning plaintext as fallback.")]
    public static partial void FailedToProtect(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Unable to unprotect field with any known protector; returning ciphertext.")]
    public static partial void UnableToUnprotect(ILogger logger);
}