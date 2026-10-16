namespace Mux.Core.Tools.Tools
{
    using System.IO;
    using System.Text;

    /// <summary>Preserves the encoding and preamble of existing text files.</summary>
    internal static class FileTextEncoding
    {
        internal static Encoding Detect(string path)
        {
            if (!File.Exists(path)) return new UTF8Encoding(false);
            using FileStream stream = File.OpenRead(path);
            byte[] bytes = new byte[4];
            int count = stream.Read(bytes, 0, bytes.Length);
            if (count >= 4 && bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0) return new UTF32Encoding(false, true);
            if (count >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff) return new UTF32Encoding(true, true);
            if (count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) return new UTF8Encoding(true);
            if (count >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return new UnicodeEncoding(false, true);
            if (count >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return new UnicodeEncoding(true, true);
            return new UTF8Encoding(false);
        }
    }
}
