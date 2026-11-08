using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskMeter
{
    // 設定一式の書き出し / 読み込み (zip)。別の PC への引っ越しや共有用。
    // 中身: settings.json (画像は images\ への相対パスに書き換え)、images\、themes\ (画像込み)、skins\ (自作のスキン)
    static class SettingsPack
    {
        public static void Export(Settings cfg, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // settings.json は今の設定を複製して、画像のパスを zip 内の相対パスにしてから入れる
                var ser = new JavaScriptSerializer();
                var copy = ser.Deserialize<Settings>(ser.Serialize(cfg));
                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // Create モードの zip は GetEntry できないので自分で覚える
                copy.BackgroundImage = PackImage(zip, cfg.BackgroundImage, added);
                copy.MascotImage = PackImage(zip, cfg.MascotImage, added);
                copy.Monitor = null; copy.MonitorId = null; copy.MonitorRect = null;   // モニターは PC ごとに違う
                foreach (Settings.FloatPos fp in copy.FloatPositions) { fp.Monitor = null; fp.MonitorId = null; fp.Rect = null; }
                AddText(zip, "settings.json", Settings.Pretty(ser.Serialize(copy)));
                AddDir(zip, Settings.ThemeDir, "themes", "*.json");
                AddDir(zip, Path.Combine(Settings.ThemeDir, "images"), "themes/images", "*.*");
                AddDir(zip, Skin.SkinDir, "skins", "*.json");
            }
        }

        // 読み込み。今の settings.json は settings.backup-日時.json に退避する。戻り値は取り込んだ内容の説明
        public static string Import(string zipPath)
        {
            string dataDir = AppPaths.DataDir;
            Directory.CreateDirectory(dataDir);
            int themes = 0, skins = 0, images = 0;
            bool settings = false;
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (name.EndsWith("/") || name.Contains("..")) continue;
                    string dest;
                    if (name == "settings.json")
                    {
                        if (File.Exists(Settings.FilePath))
                            File.Copy(Settings.FilePath, Path.Combine(dataDir, "settings.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                        dest = Settings.FilePath; settings = true;
                    }
                    else if (name.StartsWith("themes/images/")) { dest = Path.Combine(Settings.ThemeDir, "images", Path.GetFileName(name)); images++; }
                    else if (name.StartsWith("themes/") && name.EndsWith(".json")) { dest = Path.Combine(Settings.ThemeDir, Path.GetFileName(name)); themes++; }
                    else if (name.StartsWith("skins/") && name.EndsWith(".json"))
                    {
                        if (AppPaths.Redirected) { Log.Write("import " + name, new InvalidOperationException("exe のフォルダーに書き込めないのでスキンは取り込みません")); continue; }
                        dest = Path.Combine(Skin.SkinDir, Path.GetFileName(name)); skins++;
                    }
                    else if (name.StartsWith("images/")) { dest = Path.Combine(dataDir, "images", Path.GetFileName(name)); images++; }
                    else continue;
                    try { Directory.CreateDirectory(Path.GetDirectoryName(dest)); e.ExtractToFile(dest, true); }
                    catch (Exception ex) { Log.Write("import " + name, ex); if (name.StartsWith("skins/")) skins--; }
                }
            }
            // 画像の相対パス (images\...) は exe のフォルダー基準で解決されるので、保存先が別のときは絶対パスに直す
            if (settings && !string.Equals(Path.GetFullPath(dataDir).TrimEnd('\\'), Path.GetFullPath(AppPaths.Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var ser = new JavaScriptSerializer();
                    var dict = ser.DeserializeObject(File.ReadAllText(Settings.FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                    if (dict != null)
                    {
                        foreach (string key in new[] { "BackgroundImage", "MascotImage" })
                        {
                            object v;
                            if (dict.TryGetValue(key, out v) && v is string && ((string)v).StartsWith("images\\", StringComparison.OrdinalIgnoreCase))
                                dict[key] = Path.Combine(dataDir, (string)v);
                        }
                        Settings.WriteAtomic(Settings.FilePath, Settings.Pretty(ser.Serialize(dict)));
                    }
                }
                catch (Exception ex) { Log.Write("import paths", ex); }
            }
            return (settings ? "設定" : "(設定なし)") + "、テーマ " + themes + "、スキン " + skins + "、画像 " + images;
        }

        // 画像を zip の images\ に入れ、設定に書く相対パスを返す (無い・空ならそのまま)
        static string PackImage(ZipArchive zip, string setting, HashSet<string> added)
        {
            if (string.IsNullOrWhiteSpace(setting)) return setting;
            try
            {
                string full = Path.IsPathRooted(setting) ? setting : Path.Combine(AppPaths.Dir, setting);
                if (!File.Exists(full)) return setting;
                string name = Path.GetFileName(full);
                if (added.Add("images/" + name)) zip.CreateEntryFromFile(full, "images/" + name, CompressionLevel.Optimal);
                return Path.Combine("images", name);
            }
            catch (Exception ex) { Log.Write("export image " + setting, ex); return setting; }
        }

        static void AddDir(ZipArchive zip, string dir, string prefix, string pattern)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir, pattern))
            {
                try { zip.CreateEntryFromFile(f, prefix + "/" + Path.GetFileName(f), CompressionLevel.Optimal); }
                catch (Exception ex) { Log.Write("export " + f, ex); }
            }
        }

        static void AddText(ZipArchive zip, string name, string text)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream s = e.Open())
            {
                byte[] b = new UTF8Encoding(false).GetBytes(text);
                s.Write(b, 0, b.Length);
            }
        }
    }
}
