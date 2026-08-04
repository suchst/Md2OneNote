using System;
using System.Security.Cryptography;
using System.Text;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Content hashing used for page identity (FR-17) and diagram cache keys (NFR-20).
    /// Deterministic and side-effect free.
    /// </summary>
    public static class Sha256
    {
        public const string Prefix = "sha256:";

        public static string OfBytes(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            using (var sha = SHA256.Create())
            {
                return Format(sha.ComputeHash(bytes));
            }
        }

        public static string OfString(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            return OfBytes(Encoding.UTF8.GetBytes(value));
        }

        private static string Format(byte[] hash)
        {
            var builder = new StringBuilder(Prefix.Length + (hash.Length * 2));
            builder.Append(Prefix);
            for (var i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
