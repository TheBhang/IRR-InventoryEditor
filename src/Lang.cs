// Minimal two-language support: L.T("česky", "English").
using System;
using System.Globalization;
using System.IO;

namespace IrrEditor
{
    public static class L
    {
        public static bool En;

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IRR-InventoryEditor", "settings.ini"); }
        }

        /// <summary>Saved choice, otherwise Czech for Czech/Slovak Windows and English for everything else.</summary>
        public static void Init()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath))
                        if (line.StartsWith("lang=")) { En = line.Substring(5).Trim() == "en"; return; }
            }
            catch { }
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            En = two != "cs" && two != "sk";
        }

        public static void Save(bool en)
        {
            En = en;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, "lang=" + (en ? "en" : "cs") + Environment.NewLine);
            }
            catch { }
        }

        public static string T(string cz, string en) { return En ? en : cz; }
    }
}
