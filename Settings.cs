using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace jkcnsl
{
    [DataContract]
    class Settings
    {
        [DataMember]
        public string useragent { get; set; }

        static Settings _instance;
        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Settings();
                    _instance.Load();
                }
                return _instance;
            }
        }

        public void Load()
        {
            Settings settings = null;
            for (int retry = 1; retry < 20; retry++)
            {
                try
                {
                    using (var fs = new FileStream(Path.Join(AppContext.BaseDirectory, "jkcnsl.json"), FileMode.Open, FileAccess.Read))
                    {
                        settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                    }
                    break;
                }
                catch (FileNotFoundException)
                {
                    break;
                }
                catch (IOException)
                {
                    // 書き込み中など
                    Thread.Sleep(10 * retry);
                }
                catch (Exception e)
                {
                    // 内容が異常など
                    Trace.WriteLine(e.ToString());
                    break;
                }
            }

            useragent = null;
            if (settings != null)
            {
                useragent = settings.useragent;
            }
        }

        public void Save()
        {
            var settings = new Settings
            {
                useragent = useragent
            };

            for (int retry = 1; retry < 20; retry++)
            {
                try
                {
                    using (var fs = new FileStream(Path.Join(AppContext.BaseDirectory, "jkcnsl.json"), FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, settings);
                    }
                    break;
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e.ToString());
                }
                Thread.Sleep(10 * retry);
            }
        }
    }
}
