using System.Security.Cryptography;
using System.Text;

namespace Doji.AI.ModelHub
{
    internal static class TextHash
    {
        /// <summary>
        /// Lower-case hex SHA-256 of the UTF-8 bytes of <paramref name="text"/>.
        /// </summary>
        internal static string Sha256(string text)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
