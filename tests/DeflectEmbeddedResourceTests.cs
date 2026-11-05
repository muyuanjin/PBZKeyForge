using System;
using System.IO;
using System.Linq;
using System.Reflection;

public static class DeflectEmbeddedResourceTests
{
    public static int Main(string[] args)
    {
        try
        {
            var application = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            int files = 0;
            foreach (string extension in new[] { "pak", "utoc", "ucas", "json" })
            {
                string name = "PBZKeyForge.PerfectDeflect." + extension;
                using (var embedded = application.GetManifestResourceStream(name))
                using (var buffer = new MemoryStream())
                {
                    if (embedded == null) throw new Exception("Missing embedded resource: " + name);
                    embedded.CopyTo(buffer);
                    string file = extension == "json" ? "layout.json" : "PBZKeyForge_PerfectDeflect_P." + extension;
                    if (!buffer.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(args[1], file))))
                        throw new Exception("Embedded resource differs from the verified template: " + name);
                    files++;
                }
            }
            Console.WriteLine("DeflectEmbeddedResourceTests PASS: " + files + " files byte-identical to the verified templates.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
