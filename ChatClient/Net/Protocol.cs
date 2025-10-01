using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatClient.Net
{
    public enum MessageType
    {
        Message,
        Join,
        Leave,
        PrivateMessage,
        System,
        Typing,
        StopTyping
    }

    public class Protocol
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("from")]
        public string From { get; set; }

        [JsonPropertyName("to")]
        public string To { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("ts")]
        public long Timestamp { get; set; }

        [JsonPropertyName("uid")]
        public string UID { get; set; }

        public Protocol()
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static Protocol CreateMessage(string from, string text, string uid)
        {
            return new Protocol
            {
                Type = "msg",
                From = from,
                Text = text,
                UID = uid
            };
        }

        public static Protocol CreatePrivateMessage(string from, string to, string text, string uid)
        {
            return new Protocol
            {
                Type = "pm",
                From = from,
                To = to,
                Text = text,
                UID = uid
            };
        }

        public static Protocol CreateJoin(string username, string uid)
        {
            return new Protocol
            {
                Type = "join",
                From = username,
                UID = uid
            };
        }

        public static Protocol CreateLeave(string username, string uid)
        {
            return new Protocol
            {
                Type = "leave",
                From = username,
                UID = uid
            };
        }

        public static Protocol CreateSystem(string text)
        {
            return new Protocol
            {
                Type = "sys",
                Text = text
            };
        }

        public static Protocol CreateTyping(string from, string uid)
        {
            return new Protocol
            {
                Type = "typing",
                From = from,
                UID = uid
            };
        }

        public static Protocol CreateStopTyping(string from, string uid)
        {
            return new Protocol
            {
                Type = "stoptyping",
                From = from,
                UID = uid
            };
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static Protocol FromJson(string json)
        {
            return JsonSerializer.Deserialize<Protocol>(json);
        }

        public byte[] ToBytes()
        {
            var json = ToJson();
            var length = System.Text.Encoding.UTF8.GetByteCount(json);
            var buffer = new byte[4 + length];
            BitConverter.GetBytes(length).CopyTo(buffer, 0);
            System.Text.Encoding.UTF8.GetBytes(json).CopyTo(buffer, 4);
            return buffer;
        }
    }
}