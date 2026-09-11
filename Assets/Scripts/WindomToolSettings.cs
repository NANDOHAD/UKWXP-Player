using System;
using System.IO;

public static class WindomToolSettings
{
    public const string FileName = "Settings.txt";

    const string LanguagePrefix = "Language=";

    public sealed class Data
    {
        public string folder = string.Empty;
        public string languageCode = string.Empty;
    }

    public static bool Exists
    {
        get { return File.Exists(FileName); }
    }

    public static Data Load()
    {
        return Load(FileName);
    }

    public static void SaveFolder(string folder)
    {
        Data data = Load();
        data.folder = folder ?? string.Empty;
        Save(FileName, data);
    }

    public static void SaveLanguage(string languageCode)
    {
        string normalizedCode = NormalizeLanguageCode(languageCode);
        if (string.IsNullOrEmpty(normalizedCode))
            throw new ArgumentException("対応していない言語コードです。", nameof(languageCode));

        Data data = Load();
        data.languageCode = normalizedCode;
        Save(FileName, data);
    }

    public static Data Load(string path)
    {
        Data data = new Data();
        if (!File.Exists(path))
            return data;

        using (StreamReader reader = new StreamReader(path))
        {
            data.folder = reader.ReadLine() ?? string.Empty;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.StartsWith(LanguagePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                data.languageCode = NormalizeLanguageCode(line.Substring(LanguagePrefix.Length));
                break;
            }
        }

        return data;
    }

    public static void Save(string path, Data data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        using (StreamWriter writer = new StreamWriter(path, false))
        {
            writer.WriteLine(data.folder ?? string.Empty);

            string normalizedCode = NormalizeLanguageCode(data.languageCode);
            if (!string.IsNullOrEmpty(normalizedCode))
                writer.WriteLine(LanguagePrefix + normalizedCode);
        }
    }

    static string NormalizeLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return string.Empty;

        string normalizedCode = languageCode.Trim().ToLowerInvariant();
        return normalizedCode == "ja" || normalizedCode == "en" ? normalizedCode : string.Empty;
    }
}
