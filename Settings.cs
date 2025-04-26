using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace jkcnsl
{
    [DataContract]
    class Settings
    {
        [DataMember]
        public string nicovideo_cookie { get; set; }
        [DataMember]
        public string nicovideo_mfa_cookie { get; set; }
        [DataMember]
        public string mail { get; set; }
        [DataMember]
        public string password { get; set; }
        [DataMember]
        public string useragent { get; set; }
        [DataMember]
        public string device_name { get; set; }
        [DataMember]
        public bool distrust_device { get; set; }
        [DataMember]
        public double last_login_attempt { get; set; }

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

        public static string BaseDirectory { get; set; }

        public void Load()
        {
            Settings settings = null;
            for (int retry = 1; retry < 20; retry++)
            {
                try
                {
                    using (var fs = new FileStream(Path.Join(BaseDirectory ?? AppContext.BaseDirectory, "jkcnsl.json"), FileMode.Open, FileAccess.Read))
                    {
                        settings = new DataContractJsonSerializerWrapper<Settings>().ReadValue(fs);
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

            nicovideo_cookie = null;
            nicovideo_mfa_cookie = null;
            mail = null;
            password = null;
            useragent = null;
            device_name = null;
            distrust_device = false;
            last_login_attempt = 0;
            if (settings != null)
            {
                nicovideo_cookie = UnprotectString(settings.nicovideo_cookie);
                nicovideo_mfa_cookie = UnprotectString(settings.nicovideo_mfa_cookie);
                mail = UnprotectString(settings.mail);
                password = UnprotectString(settings.password);
                useragent = settings.useragent;
                device_name = settings.device_name;
                distrust_device = settings.distrust_device;
                last_login_attempt = settings.last_login_attempt;
            }
        }

        public void Save()
        {
            var settings = new Settings
            {
                nicovideo_cookie = ProtectString(nicovideo_cookie),
                nicovideo_mfa_cookie = ProtectString(nicovideo_mfa_cookie),
                mail = ProtectString(mail),
                password = ProtectString(password),
                useragent = useragent,
                device_name = device_name,
                distrust_device = distrust_device,
                last_login_attempt = last_login_attempt
            };

            for (int retry = 1; retry < 20; retry++)
            {
                try
                {
                    using (var fs = new FileStream(Path.Join(BaseDirectory ?? AppContext.BaseDirectory, "jkcnsl.json"), FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        new DataContractJsonSerializerWrapper<Settings>().WriteValue(fs, settings);
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

        static string ProtectString(string s)
        {
            if (s != null)
            {
                if (OperatingSystem.IsWindows())
                {
                    // DPAPIによる暗号化
                    return Convert.ToHexString(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.LocalMachine));
                }
                // 平文のまま
                return s;
            }
            return null;
        }

        static string UnprotectString(string s)
        {
            if (s != null)
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // DPAPIによる復号
                        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromHexString(s), null, DataProtectionScope.LocalMachine));
                    }
                    return s;
                }
                catch (Exception e)
                {
                    Trace.WriteLine(e.ToString());
                }
            }
            return null;
        }
    }
}
