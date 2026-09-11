using System.IO;
using System.Text;
using System.Threading.Tasks;

internal static class WindomVerificationStorage
{
    // One awaited writer per path. Never delete the previous result to work around a reader lock.
    internal static async Task WriteAtomicAsync(string path, string text)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                return;
            }
            catch (IOException) when (attempt < 10) { await Task.Delay(50); }
        }
    }
}
