using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace PBZKeyForge
{
    internal static class DeviceHotspots
    {
        static readonly Dictionary<string, Dictionary<string, RectangleF>> Devices = Load();

        public static Dictionary<string, RectangleF> ForDevice(string device) { return Devices[device]; }

        static Dictionary<string, Dictionary<string, RectangleF>> Load()
        {
            var result = new Dictionary<string, Dictionary<string, RectangleF>>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.Device.hotspots"))
            using (var reader = new StreamReader(stream))
            {
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                foreach (var device in data)
                {
                    var definition = (Dictionary<string, object>)device.Value;
                    var canvas = (IList)definition["canvas"];
                    float width = Convert.ToSingle(canvas[0]), height = Convert.ToSingle(canvas[1]);
                    var bounds = (Dictionary<string, object>)definition["bounds"];
                    var positions = new Dictionary<string, RectangleF>();
                    foreach (var item in bounds)
                    {
                        var box = (IList)item.Value;
                        positions.Add(item.Key, new RectangleF(Convert.ToSingle(box[0]) / width, Convert.ToSingle(box[1]) / height,
                            Convert.ToSingle(box[2]) / width, Convert.ToSingle(box[3]) / height));
                    }
                    result.Add(device.Key, positions);
                }
            }
            return result;
        }
    }
}
