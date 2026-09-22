using System.Diagnostics;
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

        public static void LaunchConnectedBrowser(string path = "")
        {
            if (Properties.Settings.Default.remoteId == string.Empty)
            {
                return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = $"https://tarkov.dev{path}?connection={Properties.Settings.Default.remoteId}",
                UseShellExecute = true,
            };
            Process.Start(psi);
        }
    }
}
