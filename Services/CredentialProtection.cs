using System;
using System.Security.Cryptography;
using System.Text;

namespace TimeMarkEdit.Services
{
    /// <summary>
    /// Protects sensitive string values (e.g. API keys) stored in plugin configuration.
    /// Encrypted values are prefixed with "ENC:" so that legacy plaintext values are
    /// detected and returned as-is, providing transparent migration on next save.
    /// </summary>
    internal static class CredentialProtection
    {
        private const string EncryptedPrefix = "ENC:";

        // Fixed application-domain salt — not secret, just provides key separation.
        private const string AppSalt = "TimeMarkEdit-TheIntroDB-v1";
        private const int Iterations = 100_000;

        // Derives an AES-256 key from the machine name so the encrypted blob is
        // unreadable on another machine even if the config file is copied.
        private static byte[] DeriveKey()
        {
            var password = Encoding.UTF8.GetBytes(Environment.MachineName);
            var salt = Encoding.UTF8.GetBytes(AppSalt);
            using var kdf = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(32); // AES-256
        }

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> and returns a Base64 blob prefixed with "ENC:".
        /// Returns the original value unchanged if it is null or empty.
        /// </summary>
        public static string Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                return string.Empty;

            var key = DeriveKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            using var encryptor = aes.CreateEncryptor();
            var cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // Layout: IV (16 bytes) || ciphertext
            var blob = new byte[16 + cipher.Length];
            Buffer.BlockCopy(aes.IV, 0, blob, 0, 16);
            Buffer.BlockCopy(cipher, 0, blob, 16, cipher.Length);

            return EncryptedPrefix + Convert.ToBase64String(blob);
        }

        /// <summary>
        /// Decrypts a value produced by <see cref="Protect"/>.
        /// If <paramref name="stored"/> does not start with "ENC:" it is assumed to be a
        /// legacy plaintext value and returned as-is (transparent migration).
        /// Returns an empty string on any failure.
        /// </summary>
        public static string Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored))
                return string.Empty;

            // Legacy plaintext — return as-is until the user saves again.
            if (!stored.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                return stored;

            try
            {
                var blob = Convert.FromBase64String(stored[EncryptedPrefix.Length..]);
                if (blob.Length < 17) // Need at least IV (16) + 1 cipher byte
                    return string.Empty;

                var iv = new byte[16];
                var cipher = new byte[blob.Length - 16];
                Buffer.BlockCopy(blob, 0, iv, 0, 16);
                Buffer.BlockCopy(blob, 16, cipher, 0, cipher.Length);

                var key = DeriveKey();
                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var decryptor = aes.CreateDecryptor();
                var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // Decryption failure (e.g. key mismatch after machine rename).
                // Return empty — the user will need to re-enter the key.
                return string.Empty;
            }
        }
    }
}
