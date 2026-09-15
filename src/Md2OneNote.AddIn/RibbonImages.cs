using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// Serves the ribbon's <c>loadImage</c> callback from PNGs embedded in this assembly.
    /// </summary>
    /// <remarks>
    /// <b>OneNote wants an <c>IStream</c>, not an <c>IPictureDisp</c>.</b> Word and Excel take
    /// either; OneNote's ribbon silently shows a blank button for a picture object (2026-09-15).
    /// Microsoft's OneNote add-in sample and OneMore both hand back a stream of PNG bytes, and
    /// that is what this does: an OLE memory stream, which marshals from the surrogate to
    /// OneNote by value. A fresh stream per call, because a stream has a position and Office
    /// reads each one once.
    /// </remarks>
    internal static class RibbonImages
    {
        private const int StreamSeekSet = 0;

        /// <summary>A stream of the PNG for an image id, or null when no such resource is embedded.</summary>
        public static IStream Load(string imageId)
        {
            var bytes = ReadEmbedded(imageId);
            if (bytes == null)
            {
                return null;
            }

            IStream stream;
            var result = NativeMethods.CreateStreamOnHGlobal(IntPtr.Zero, true, out stream);
            if (result != 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            stream.Write(bytes, bytes.Length, IntPtr.Zero);
            stream.Seek(0, StreamSeekSet, IntPtr.Zero);
            return stream;
        }

        private static byte[] ReadEmbedded(string imageId)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = typeof(RibbonImages).Namespace + ".Images." + imageId + ".png";

            using (var stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }

        private static class NativeMethods
        {
            [DllImport("ole32.dll")]
            public static extern int CreateStreamOnHGlobal(IntPtr hGlobal, bool deleteOnRelease, out IStream stream);
        }
    }
}
