using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// Resolves the add-in's own assemblies from the folder it was loaded from.
    /// </summary>
    /// <remarks>
    /// A COM-activated assembly runs inside a host that knows nothing about it: the AppDomain's base
    /// directory is ONENOTE.EXE's, not ours, so the default probing logic looks for Md2OneNote.Core,
    /// Markdig and the rest in the wrong place. Registration supplies a <c>CodeBase</c> for the
    /// entry assembly alone; everything it depends on has to be found by us.
    /// <para>
    /// Deliberately free of any dependency outside mscorlib, and installed from
    /// <see cref="Connect"/>'s static constructor, so that it is in place before any method
    /// referencing another assembly is JIT-compiled.
    /// </para>
    /// </remarks>
    internal static class AssemblyResolver
    {
        private static int _installed;

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var folder = OurFolder();
                if (folder == null)
                {
                    return null;
                }

                var simpleName = new AssemblyName(args.Name).Name;
                var candidate = Path.Combine(folder, simpleName + ".dll");

                // Returning null for anything we do not have lets the normal rules continue; this
                // handler exists to add a probing location, not to take over binding.
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string OurFolder()
        {
            var assembly = typeof(AssemblyResolver).Assembly;

            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location);
            }

            // A CodeBase-loaded assembly may report no Location.
            var codeBase = assembly.CodeBase;
            if (string.IsNullOrEmpty(codeBase))
            {
                return null;
            }

            return Path.GetDirectoryName(new Uri(codeBase).LocalPath);
        }
    }
}
