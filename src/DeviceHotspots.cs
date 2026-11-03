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
        static readonly Dictionary<string, Dictionary<string, PointF>> Devices = Load();

        public static Dictionary<string, PointF> ForDevice(string device) { return Devices[device]; }

        static Dictionary<string, Dictionary<string, PointF>> Load()
        {
            var result = new Dictionary<string, Dictionary<string, PointF>>();
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PBZKeyForge.Device.hotspots"))
            using (var reader = new StreamReader(stream))
            {
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                foreach (var device in data)
                {
                    var points = (Dictionary<string, object>)((Dictionary<string, object>)device.Value)["points"];
                    var positions = new Dictionary<string, PointF>();
                    foreach (var point in points)
                    {
                        var xy = (IList)point.Value;
                        positions.Add(point.Key, new PointF(Convert.ToSingle(xy[0]), Convert.ToSingle(xy[1])));
                    }
                    result.Add(device.Key, positions);
                }
            }
            return result;
        }
    }
}
