using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DisplayVeil
{
    [DataContract]
    internal sealed class Settings
    {
        [DataMember] public string ViewingId = "";
        [DataMember] public bool AutoCover = true;
        [DataMember] public bool ShowMouseHints = true;
        [DataMember] public string ToggleKey = "F9";
        [DataMember] public string RevealKey = "F10";
        [DataMember] public string StopKey = "F11";

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            AutoCover = true; ShowMouseHints = true;
            ToggleKey = "F9"; RevealKey = "F10"; StopKey = "F11";
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string path;
        public SettingsStore(string directory) { path = Path.Combine(directory, "settings.json"); }
        public Settings Load(out string warning)
        {
            warning = "";
            if (!File.Exists(path)) return new Settings();
            try
            {
                using (var stream = File.OpenRead(path))
                    return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream) ?? new Settings();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException || ex is System.Xml.XmlException)) throw;
                warning = "設定を読み込めなかったため、初期設定で起動しました。";
                return new Settings();
            }
        }
        public bool Save(Settings settings, out string error)
        {
            error = "";
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SerializationException)) throw;
                error = "設定を保存できません。この起動中の変更は有効です。";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
