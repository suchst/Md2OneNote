using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// The result of the DevTools protocol's <c>Page.captureScreenshot</c>: one base64 field.
    /// </summary>
    [DataContract]
    public sealed class ScreenshotReply
    {
        [DataMember(Name = "data")]
        public string Data { get; set; }

        /// <summary>The decoded image, or null when the reply carries none.</summary>
        public static byte[] TryDecode(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var reply = (ScreenshotReply)new DataContractJsonSerializer(typeof(ScreenshotReply)).ReadObject(stream);
                    return reply == null || string.IsNullOrEmpty(reply.Data)
                        ? null
                        : Convert.FromBase64String(reply.Data);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is FormatException || ex is InvalidCastException)
            {
                return null;
            }
        }
    }
}
