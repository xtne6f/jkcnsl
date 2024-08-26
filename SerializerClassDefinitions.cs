using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace jkcnsl
{
    public class Lives
    {
        public LivesData data { get; set; }
        public LivesMeta meta { get; set; }
    }

    public class LivesData
    {
        public List<LivesDataItem> items { get; set; }
    }

    public class LivesMeta
    {
        public double status { get; set; }
    }

    public class LivesDataItem
    {
        public string category { get; set; }
        public double id { get; set; }
    }

    public class WatchEmbedded
    {
        public WatchEmbeddedSite site { get; set; }
        public WatchEmbeddedUser user { get; set; }
    }

    public class WatchEmbeddedSite
    {
        public WatchEmbeddedRelive relive { get; set; }
    }

    public class WatchEmbeddedUser
    {
        public string id { get; set; }
        public bool isLoggedIn { get; set; }
        public string nickname { get; set; }
    }

    public class WatchEmbeddedRelive
    {
        public string webSocketUrl { get; set; }
    }

    public class WatchSessionResult
    {
        public string type { get; set; }
    }

    public class WatchSessionResultForError
    {
        public WatchSessionResultError data { get; set; }
    }

    public class WatchSessionResultForMessageServer
    {
        public WatchSessionResultMessageServer data { get; set; }
    }

    public class WatchSessionResultForRoom
    {
        public WatchSessionResultRoom data { get; set; }
    }

    public class WatchSessionResultForSeat
    {
        public WatchSessionResultSeat data { get; set; }
    }

    public class WatchSessionResultForServerTime
    {
        public WatchSessionResultServerTime data { get; set; }
    }

    public class WatchSessionResultError
    {
        public string code { get; set; }
    }

    public class WatchSessionResultMessageServer
    {
        public string viewUri { get; set; }
        public string vposBaseTime { get; set; }
        public string hashedUserId { get; set; }
    }

    public class WatchSessionResultServerTime
    {
        public string currentMs { get; set; }
    }

    public class WatchSessionResultRoom
    {
        public WatchSessionResultRoomMessageServer messageServer { get; set; }
        public string threadId { get; set; }
        public string yourPostKey { get; set; }
        public string vposBaseTime { get; set; }
    }

    public class WatchSessionResultSeat
    {
        public double keepIntervalSec { get; set; }
    }

    public class WatchSessionResultRoomMessageServer
    {
        public string uri { get; set; }
    }

    public class CommentSessionResult
    {
        public CommentSessionResultChat chat { get; set; }
        public ContentContainer ping { get; set; }
        public CommentSessionResultThread thread { get; set; }
    }

    public class CommentSessionResultChat
    {
        public double anonymity { get; set; }
        public string content { get; set; }
        public double date { get; set; }
        public double date_usec { get; set; }
        public string mail { get; set; }
        public double no { get; set; }
        public double premium { get; set; }
        public string thread { get; set; }
        public string user_id { get; set; }
        public double vpos { get; set; }
        public double yourpost { get; set; }
    }

    public class CommentSessionResultThread
    {
        public string content { get; set; }
        public double last_res { get; set; }
        public double resultcode { get; set; }
        public double revision { get; set; }
        public double server_time { get; set; }
        public string thread { get; set; }
        public string ticket { get; set; }
    }

    [DataContract]
    public class CommentSessionOpen
    {
        [DataMember(EmitDefaultValue = false)]
        public ContentContainer ping { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public CommentSessionOpenThread thread { get; set; }
    }

    [DataContract]
    public class CommentSessionOpenThread
    {
        [DataMember]
        public double nicoru { get; set; }
        [DataMember]
        public double res_from { get; set; }
        [DataMember]
        public double scores { get; set; }
        [DataMember]
        public string thread { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public string threadkey { get; set; }
        [DataMember]
        public string user_id { get; set; }
        [DataMember]
        public string version { get; set; }
        [DataMember]
        public double with_global { get; set; }
    }

    public class WatchSessionPost
    {
        public WatchSessionPostData data { get; set; }
        public string type { get; set; }
    }

    [DataContract]
    public class WatchSessionPostData
    {
        [DataMember(EmitDefaultValue = false)]
        public string color { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public string font { get; set; }
        [DataMember]
        public bool isAnonymous { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public string position { get; set; }
        [DataMember(EmitDefaultValue = false)]
        public string size { get; set; }
        [DataMember]
        public string text { get; set; }
        [DataMember]
        public double vpos { get; set; }
    }

    public class ContentContainer
    {
        public string content { get; set; }
    }

    /// <summary>
    /// DataContractJsonSerializerのジェネリックなラッパー
    /// トリミング(PublishTrimmed)する場合は型Tがトリム対象にならないよう注意
    /// </summary>
    class DataContractJsonSerializerWrapper<T>
    {
        readonly DataContractJsonSerializer _js = new DataContractJsonSerializer(typeof(T));

        public T ReadValue(Stream s)
        {
            return (T)_js.ReadObject(s);
        }

        public T ReadValue(byte[] buf)
        {
            return ReadValue(new MemoryStream(buf));
        }

        public T ReadValue(byte[] buf, int index, int count)
        {
            return ReadValue(new MemoryStream(buf, index, count));
        }

        public void WriteValue(Stream s, T val)
        {
            _js.WriteObject(s, val);
        }
    }
}
