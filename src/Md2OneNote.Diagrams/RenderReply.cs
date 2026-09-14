using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// What the shell posts back after <c>renderDiagram</c> or <c>afterFrame</c>:
    /// <c>{"id":"…","ok":true,"w":720,"h":410}</c> or <c>{"id":"…","ok":false,"error":"…"}</c>.
    /// </summary>
    [DataContract]
    internal sealed class RenderReply
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "ok")]
        public bool Ok { get; set; }

        [DataMember(Name = "w")]
        public int Width { get; set; }

        [DataMember(Name = "h")]
        public int Height { get; set; }

        [DataMember(Name = "error")]
        public string Error { get; set; }

        /// <summary>
        /// Parses a message from the shell. Returns null for anything that is not a reply, which
        /// the host ignores: the shell is the only script that runs, but a message it did not
        /// mean to send is still not a reason to fault the renderer.
        /// </summary>
        public static RenderReply TryParse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(RenderReply));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var reply = serializer.ReadObject(stream) as RenderReply;
                    return reply == null || string.IsNullOrEmpty(reply.Id) ? null : reply;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>Escapes a .NET string as a JavaScript string literal, double-quoted.</summary>
    internal static class JsLiteral
    {
        public static string Quote(string value)
        {
            value = value ?? string.Empty;
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\u2028': sb.Append("\\u2028"); break;
                    case '\u2029': sb.Append("\\u2029"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
