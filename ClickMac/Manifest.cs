﻿using System;
using System.Xml.Linq;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Security.Cryptography;

namespace ClickMac
{
    public class Manifest
    {
        enum ns { asmv1, asmv2, asmv3, cov1, cov2, dsig }
        private static XName xname(string localName, ns Ns)
        {
            string NameSpace = "";
            switch (Ns)
            {
                case ns.asmv1:
                    NameSpace = "urn:schemas-microsoft-com:asm.v1";
                    break;
                case ns.asmv2:
                    NameSpace = "urn:schemas-microsoft-com:asm.v2";
                    break;
                case ns.asmv3:
                    NameSpace = "urn:schemas-microsoft-com:asm.v3";
                    break;
                case ns.cov1:
                    NameSpace = "urn:schemas-microsoft-com:clickonce.v1";
                    break;
                case ns.cov2:
                    NameSpace = "urn:schemas-microsoft-com:clickonce.v2";
                    break;
                case ns.dsig:
                    NameSpace = "http://www.w3.org/2000/09/xmldsig#";
                    break;
            }
            return XName.Get(localName, NameSpace);
        }
        public static string getUrlFolder(string url)
        {
            return url.Substring(0, url.LastIndexOf('/'));
        }
        public static string FixFileSeperator(string path)
        {
            if (String.IsNullOrEmpty(path))
                return path;
            return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }


        public XDocument Xml;
        public String Location;
        public string DiskLocation;
        public string Subfolder;

        public EntryPoint entry = new EntryPoint();

        public EntryPoint Entry
        {
            get
            {
                return entry;
            }
        }

        public List<Manifest> Children { get; private set; } = new List<Manifest>();

        public Manifest(string Uri, string subfolder = "")
        {
            Subfolder = subfolder;
            Location = Uri;
            Xml = XDocument.Load(Uri);
            VerifySignature();

            DiskLocation = Xml.Root.Element(xname("assemblyIdentity", ns.asmv1)).Attribute("name").Value;

            var deployment = Xml.Root.Element(xname("deployment", ns.asmv2));
            if (deployment != null && deployment.Element(xname("deploymentProvider", ns.asmv2)) != null)
            {
                Location = deployment.Element(xname("deploymentProvider", ns.asmv2)).Attribute("codebase").Value;
                entry.DeploymentProviderUrl = Location;
                XDocument newManifest = null;
                try
                {
                    Loading.Log("Getting updated manifest from {0}", Location);
                    newManifest = XDocument.Load(new WebClient().OpenRead(Location));
                    newManifest.Save(DiskLocation);
                }
                catch (WebException)
                {
                    Loading.Log("Getting manifest failed. Starting in Offline Mode");
                    newManifest = XDocument.Load(DiskLocation);
                }
            }
        }

        private bool VerifySignature()
        {
            var xdoc = new XmlDocument();
            xdoc.PreserveWhitespace = true;
            xdoc.Load(Location);
            SignedXml signed = new SignedXml(xdoc);

            XmlNodeList nodeList = xdoc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
            if (nodeList.Count == 0)
                return false;
            signed.LoadXml((XmlElement)nodeList[0]);
            AsymmetricAlgorithm key = null;
            var res = signed.CheckSignatureReturningKey(out key);
            return res;
        }

        public void ProcessDependencies()
        {
            var path = getUrlFolder(Location);
            foreach (var dependency in Xml.Root.Elements(xname("dependency", ns.asmv2)))
            {
                ProcessDependency(dependency, path);
            }
        }

        private void ProcessDependency(XElement dependency, string path)
        {
            var dependentAssembly = dependency.Element(xname("dependentAssembly", ns.asmv2));
            if (dependentAssembly == null || dependentAssembly.Attribute("dependencyType").Value != "install")
                return;
            var codebase = FixFileSeperator(dependentAssembly.Attribute("codebase").Value);
            var assemblyIdentity = dependentAssembly.Element(xname("assemblyIdentity", ns.asmv2));
            string version = String.Format("{0}_{1}", assemblyIdentity.Attribute("name").Value, assemblyIdentity.Attribute("version").Value);
            Directory.CreateDirectory(version);
            try
            {
                foreach (var deploy in Directory.EnumerateFiles(version, "*.deploy", SearchOption.AllDirectories))
                {
                    var dest = deploy.Substring(0, deploy.Length - ".deploy".Length);
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(deploy, dest);
                }
            }
            catch (IOException) { }
            string filename = Path.Combine(".", version, Path.GetFileName(codebase));
            bool downloaded = false;
            if (File.Exists(filename))
            {
                if (new FileInfo(filename).Length == int.Parse(dependentAssembly.Attribute("size").Value)) // HACK: Not an actual equality test. (Although it's usually good enough)
                {
                    downloaded = true;
                }
                else
                    File.Move(filename, filename + "._");
            }
            if (!downloaded)
            {
                try
                {
                    Loading.Log("Getting Dependency {0}", codebase);
                    new WebClient().DownloadFile(path + "/" + codebase.Replace('\\', '/'), filename);
                }
                catch (WebException)
                {
                    try
                    {
                        new WebClient().DownloadFile(path + "/" + codebase.Replace('\\', '/') + ".deploy", filename);
                    }
                    catch (WebException)
                    {
                        if (File.Exists(filename + "._"))
                            File.Move(filename + "._", filename);
                        else
                        {
                            Loading.Log("\tFailed to download!  Application might not work.");
                            return;
                        }
                    }
                }
            }
            if (File.Exists(filename + "._"))
                File.Delete(filename + "._");
            if (Path.GetExtension(codebase) == ".manifest")
            {
                var manifest = new Manifest(path + "/" + codebase.Replace('\\', '/'), version);
                manifest.ProcessDependencies();
                Children.Add(manifest);
                entry.Import(manifest.entry);
            }
            foreach (var file in Xml.Root.Elements(xname("file", ns.asmv2)))
            {
                GetFile(file, getUrlFolder(path + "/" + codebase.Replace('\\', '/')));
            }
            foreach (var fa in Xml.Root.Elements(xname("fileAssociation", ns.cov1)))
            {
                Platform.AssociateFile(fa);
            }
            var entryPoint = Xml.Root.Element(xname("entryPoint", ns.asmv2));
            if (entryPoint != null)
            {
                entry.executable = entryPoint.Element(xname("commandLine", ns.asmv2)).Attribute("file").Value;
                entry.folder = new DirectoryInfo(Subfolder ?? version).FullName; // Alsolute reference.
                entry.version = assemblyIdentity.Attribute("version").Value;
                entry.displayName = entryPoint.Element(xname("assemblyIdentity", ns.asmv2)).Attribute("name").Value;
            }
            var description = Xml.Root.Element(xname("description", ns.asmv1));
            if (description != null)
            {
                var iconFile = description.Attribute(xname("iconFile", ns.asmv2));
                if (iconFile != null && !string.IsNullOrWhiteSpace(iconFile.Value))
                {
                    if (File.Exists(Path.Combine(Subfolder ?? version, iconFile.Value)))
                    {
                        entry.icon = Path.Combine(Subfolder ?? version, iconFile.Value);
                    }
                }
            }
            if (!String.IsNullOrWhiteSpace(Subfolder))
            {
                if (!File.Exists(Path.Combine(Subfolder, Path.GetFileName(codebase))))
                    File.Copy(Path.Combine(".", version, Path.GetFileName(codebase)), Path.Combine(Subfolder, Path.GetFileName(codebase)));
            }
        }
        private void GetFile(XElement file, string path)
        {

            string name = file.Attribute("name").Value;
            string filename = Path.Combine(Subfolder, name.Replace('\\', Path.DirectorySeparatorChar));
            if (!Directory.Exists(Path.GetDirectoryName(filename)))
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
            Console.WriteLine("Getting {0}", filename);
            bool downloaded = false;
            if (File.Exists(filename))
            {
                if (new FileInfo(filename).Length == int.Parse(file.Attribute("size").Value))
                {
                    downloaded = true;
                }
                else
                    File.Move(filename, filename + "._");
            }
            if (!downloaded)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
                try
                {
                    new WebClient().DownloadFile(path + "/" + name.Replace('\\', '/'), filename);
                }
                catch (WebException)
                {
                    try
                    {
                        new WebClient().DownloadFile(path + "/" + name.Replace('\\', '/') + ".deploy", filename);
                    }
                    catch (WebException)
                    {
                        if (File.Exists(filename + "._"))
                            File.Move(filename + "._", filename);
                        else
                        {
                            Loading.Log("\tFailed to download!  Application might not work.");
                            return;
                        }
                    }
                }
                if (File.Exists(filename + "._"))
                    File.Delete(filename + "._");
            }
        }

    }
}
