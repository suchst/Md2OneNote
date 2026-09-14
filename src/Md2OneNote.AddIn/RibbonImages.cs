using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// Serves the ribbon's <c>loadImage</c> callback from PNGs embedded in this assembly.
    /// </summary>
    /// <remarks>
    /// Office wants an OLE <c>IPictureDisp</c>, not a GDI+ <see cref="Image"/>. The only
    /// managed way to make one without referencing <c>stdole</c> is the protected
    /// <see cref="AxHost.GetIPictureDispFromPicture"/>, hence the private subclass. The picture
    /// crosses the process boundary from the surrogate to OneNote through OLE's own marshalling;
    /// the same route serves OneMore's custom ribbon icons, so it is known to work.
    /// <para>
    /// Images are cached: Office asks for each id once per ribbon load, but a failed lookup
    /// (typo in <c>Ribbon.xml</c>) is logged rather than thrown, because a throw here would
    /// reach Office as a ribbon error and cost the whole tab.
    /// </para>
    /// </remarks>
    internal static class RibbonImages
    {
        private static readonly Dictionary<string, object> Cache =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The picture for an image id, or null when no such resource is embedded.</summary>
        public static object Load(string imageId)
        {
            lock (Cache)
            {
                object picture;
                if (Cache.TryGetValue(imageId, out picture))
                {
                    return picture;
                }

                var image = ReadEmbedded(imageId);
                picture = image == null ? null : PictureConverter.ToPictureDisp(image);
                Cache[imageId] = picture;
                return picture;
            }
        }

        private static Image ReadEmbedded(string imageId)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = typeof(RibbonImages).Namespace + ".Images." + imageId + ".png";

            using (var stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    return null;
                }

                // Image.FromStream keeps the stream; copy so the resource stream can close.
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    copy.Position = 0;
                    return new Bitmap(copy);
                }
            }
        }

        private sealed class PictureConverter : AxHost
        {
            // AxHost needs some CLSID to exist; it is never instantiated as a control.
            private PictureConverter() : base("63109182-966B-4E3C-A8B2-8BC4A88D221C")
            {
            }

            public static object ToPictureDisp(Image image)
            {
                return GetIPictureDispFromPicture(image);
            }
        }
    }
}
