﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ClickMac
{
    static class ApplicationStore
    {
        public static XDocument LoadLatestOfflineManifest(string Uri)
        {
            switch (Path.GetExtension(Uri))
            {
                case ".application":
            var AppName = Path.GetFileNameWithoutExtension(Uri);
            var options = Directory.GetFiles(Path.Combine(Platform.LibraryLocation, "Manifests"), AppName + "*.application");
            if (options.Length == 0)
                return null;
            if (options.Length == 1)
                return XDocument.Load(options[0]);

            return XDocument.Load(options.Last());

                case ".manifest":
                    return null;
                default:
                    throw new NotImplementedException();
            }
        }
        public static void Install(Manifest manifest)
        {
            var dest = Path.Combine(Platform.LibraryLocation, "Manifests", manifest.Identity + ".application");
            manifest.Xml.Save(dest);

        }

        public static void Uninstall(Manifest manifest)
        {
            Uninstall(manifest.Identity);
        }

        public static void Uninstall(string identity)
        {
            var m = Path.Combine(Platform.LibraryLocation, "Manifests", identity + ".application");
            if (File.Exists(m))
                File.Delete(m);
            Cleanup();

        }

        private static void Cleanup()
        {
            // TODO [6] : Delete all bar the latest two versions of each installed app, and remove all unused dependancies.
            // This will prevent unneeded disk bloating, and prevent buildup of too many old versions.
            // It will also allow for true uninstallation (As compared to what currently happens)

        }
    }
}
