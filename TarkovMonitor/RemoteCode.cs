using System.Security.Cryptography;
using System.Text;

namespace TarkovMonitor
{
    internal class RemoteCode
    {
        const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        public static string Generate(int length = 16)
        {
            var result = new StringBuilder(length);
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] buffer = new byte[4];
                for (int i = 0; i < length; i++)
                {
                    rng.GetBytes(buffer);
                    uint num = BitConverter.ToUInt32(buffer, 0);
                    result.Append(Chars[(int)(num % (uint)Chars.Length)]);
                }
            }
            return result.ToString();
        }
    }
}
