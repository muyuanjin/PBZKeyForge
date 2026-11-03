using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PBZKeyForge
{
    internal sealed class DeviceArt
    {
        public float Width = 24f;
        public float Height = 24f;
        public float Stroke = 2f;
        public readonly List<GraphicsPath> Figures = new List<GraphicsPath>();

        public void Draw(Graphics graphics, RectangleF bounds, Color color)
        {
            if (Figures.Count == 0 || bounds.Width <= 0f || bounds.Height <= 0f) return;
            float scale = Math.Min(bounds.Width / Width, bounds.Height / Height);
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TranslateTransform(bounds.X + (bounds.Width - Width * scale) / 2f, bounds.Y + (bounds.Height - Height * scale) / 2f);
                graphics.ScaleTransform(scale, scale);
                using (var pen = new Pen(color, Stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    foreach (GraphicsPath figure in Figures) graphics.DrawPath(pen, figure);
            }
            finally { graphics.Restore(state); }
        }
    }

    internal static class DeviceArtwork
    {
        static readonly DeviceArt Mouse = Load("PBZKeyForge.DeviceMouse");
        static readonly DeviceArt Pad = Load("PBZKeyForge.DevicePad");

        public static void Paint(Graphics graphics, RectangleF bounds, bool pad, Color color)
        {
            DeviceArt art = pad ? Pad : Mouse;
            if (art != null) art.Draw(graphics, bounds, color);
        }

        static DeviceArt Load(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
        }

        internal static DeviceArt Parse(string markup)
        {
            var art = new DeviceArt();
            Match box = Regex.Match(markup, "viewBox\\s*=\\s*\"([^\"]*)\"");
            if (box.Success)
            {
                float[] size = Numbers(box.Groups[1].Value, 4);
                if (size != null && size[2] > 0f && size[3] > 0f) { art.Width = size[2]; art.Height = size[3]; }
            }
            Match stroke = Regex.Match(markup, "stroke-width\\s*=\\s*\"([0-9.]+)\"");
            if (stroke.Success) art.Stroke = float.Parse(stroke.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (Match element in Regex.Matches(markup, "<(path|circle|rect|line)\\b([^>]*)>"))
            {
                string shape = element.Groups[1].Value, attributes = element.Groups[2].Value;
                var figure = new GraphicsPath();
                if (shape == "path")
                {
                    string data = Attribute(attributes, "d");
                    if (data != null) AppendPath(figure, data);
                }
                else if (shape == "circle") AddCircle(figure, attributes);
                else if (shape == "rect") AddRect(figure, attributes);
                else AddLine(figure, attributes);
                if (figure.PointCount > 0) art.Figures.Add(figure); else figure.Dispose();
            }
            return art;
        }

        static string Attribute(string attributes, string name)
        {
            Match match = Regex.Match(attributes, "\\b" + name + "\\s*=\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        static float AttributeValue(string attributes, string name, float fallback)
        {
            float[] values = Numbers(Attribute(attributes, name), 1);
            return values == null ? fallback : values[0];
        }

        static float[] Numbers(string text, int count)
        {
            if (text == null) return null;
            MatchCollection matches = Regex.Matches(text, "[-+]?(?:[0-9]+\\.?[0-9]*|\\.[0-9]+)(?:[eE][-+]?[0-9]+)?");
            if (matches.Count < count) return null;
            var values = new float[count];
            for (int i = 0; i < count; i++) values[i] = float.Parse(matches[i].Value, CultureInfo.InvariantCulture);
            return values;
        }

        static void AddCircle(GraphicsPath path, string attributes)
        {
            float cx = AttributeValue(attributes, "cx", 0f), cy = AttributeValue(attributes, "cy", 0f), radius = AttributeValue(attributes, "r", 0f);
            if (radius <= 0f) return;
            path.AddEllipse(cx - radius, cy - radius, radius * 2f, radius * 2f);
        }

        static void AddLine(GraphicsPath path, string attributes)
        {
            path.AddLine(AttributeValue(attributes, "x1", 0f), AttributeValue(attributes, "y1", 0f),
                AttributeValue(attributes, "x2", 0f), AttributeValue(attributes, "y2", 0f));
        }

        static void AddRect(GraphicsPath path, string attributes)
        {
            float x = AttributeValue(attributes, "x", 0f), y = AttributeValue(attributes, "y", 0f);
            float width = AttributeValue(attributes, "width", 0f), height = AttributeValue(attributes, "height", 0f);
            if (width <= 0f || height <= 0f) return;
            float radius = Math.Min(AttributeValue(attributes, "rx", 0f), Math.Min(width, height) / 2f);
            if (radius <= 0f) { path.AddRectangle(new RectangleF(x, y, width, height)); return; }
            float diameter = radius * 2f;
            path.StartFigure();
            path.AddArc(x, y, diameter, diameter, 180f, 90f);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270f, 90f);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
        }

        static void AppendPath(GraphicsPath path, string data)
        {
            int index = 0;
            char command = '\0';
            float x = 0f, y = 0f, startX = 0f, startY = 0f;
            float cubicX = 0f, cubicY = 0f, quadX = 0f, quadY = 0f;
            bool afterCubic = false, afterQuad = false;
            while (true)
            {
                SkipSeparators(data, ref index);
                if (index >= data.Length) break;
                if (char.IsLetter(data[index]))
                {
                    command = data[index++];
                    if (command == 'Z' || command == 'z')
                    {
                        path.CloseFigure(); x = startX; y = startY; afterCubic = afterQuad = false; command = '\0';
                        continue;
                    }
                }
                else if (command == 'M') command = 'L';
                else if (command == 'm') command = 'l';
                else if (command == '\0') throw new FormatException("SVG path data has no command at index " + index.ToString(CultureInfo.InvariantCulture));

                bool relative = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                    {
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { dx += x; dy += y; }
                        x = dx; y = dy;
                        startX = x; startY = y;
                        path.StartFigure();
                        afterCubic = afterQuad = false;
                        break;
                    }
                    case 'L':
                    {
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { dx += x; dy += y; }
                        path.AddLine(x, y, dx, dy); x = dx; y = dy;
                        afterCubic = afterQuad = false;
                        break;
                    }
                    case 'H':
                    {
                        float dx = Number(data, ref index);
                        if (relative) dx += x;
                        path.AddLine(x, y, dx, y); x = dx;
                        afterCubic = afterQuad = false;
                        break;
                    }
                    case 'V':
                    {
                        float dy = Number(data, ref index);
                        if (relative) dy += y;
                        path.AddLine(x, y, x, dy); y = dy;
                        afterCubic = afterQuad = false;
                        break;
                    }
                    case 'C':
                    {
                        float c1x = Number(data, ref index), c1y = Number(data, ref index);
                        float c2x = Number(data, ref index), c2y = Number(data, ref index);
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { c1x += x; c1y += y; c2x += x; c2y += y; dx += x; dy += y; }
                        path.AddBezier(x, y, c1x, c1y, c2x, c2y, dx, dy);
                        cubicX = c2x; cubicY = c2y; x = dx; y = dy;
                        afterCubic = true; afterQuad = false;
                        break;
                    }
                    case 'S':
                    {
                        float c2x = Number(data, ref index), c2y = Number(data, ref index);
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { c2x += x; c2y += y; dx += x; dy += y; }
                        float c1x = afterCubic ? x * 2f - cubicX : x, c1y = afterCubic ? y * 2f - cubicY : y;
                        path.AddBezier(x, y, c1x, c1y, c2x, c2y, dx, dy);
                        cubicX = c2x; cubicY = c2y; x = dx; y = dy;
                        afterCubic = true; afterQuad = false;
                        break;
                    }
                    case 'Q':
                    {
                        float qx = Number(data, ref index), qy = Number(data, ref index);
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { qx += x; qy += y; dx += x; dy += y; }
                        path.AddBezier(x, y, x + (qx - x) * 2f / 3f, y + (qy - y) * 2f / 3f, dx + (qx - dx) * 2f / 3f, dy + (qy - dy) * 2f / 3f, dx, dy);
                        quadX = qx; quadY = qy; x = dx; y = dy;
                        afterQuad = true; afterCubic = false;
                        break;
                    }
                    case 'T':
                    {
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { dx += x; dy += y; }
                        float qx = afterQuad ? x * 2f - quadX : x, qy = afterQuad ? y * 2f - quadY : y;
                        path.AddBezier(x, y, x + (qx - x) * 2f / 3f, y + (qy - y) * 2f / 3f, dx + (qx - dx) * 2f / 3f, dy + (qy - dy) * 2f / 3f, dx, dy);
                        quadX = qx; quadY = qy; x = dx; y = dy;
                        afterQuad = true; afterCubic = false;
                        break;
                    }
                    case 'A':
                    {
                        float rx = Number(data, ref index), ry = Number(data, ref index);
                        float angle = Number(data, ref index);
                        bool large = Number(data, ref index) != 0f, sweep = Number(data, ref index) != 0f;
                        float dx = Number(data, ref index), dy = Number(data, ref index);
                        if (relative) { dx += x; dy += y; }
                        AppendArc(path, x, y, rx, ry, angle, large, sweep, dx, dy);
                        x = dx; y = dy;
                        afterCubic = afterQuad = false;
                        break;
                    }
                    default:
                        throw new FormatException("SVG path data has an unsupported command '" + command.ToString() + "'");
                }
            }
        }

        static void AppendArc(GraphicsPath path, float x1, float y1, float rx, float ry, float angle, bool large, bool sweep, float x2, float y2)
        {
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            if (rx == 0f || ry == 0f || (x1 == x2 && y1 == y2)) { path.AddLine(x1, y1, x2, y2); return; }
            double phi = angle * Math.PI / 180.0, cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
            double halfX = (x1 - x2) / 2.0, halfY = (y1 - y2) / 2.0;
            double localX = cosPhi * halfX + sinPhi * halfY, localY = -sinPhi * halfX + cosPhi * halfY;
            double radiusX = rx, radiusY = ry, ratio = localX * localX / (radiusX * radiusX) + localY * localY / (radiusY * radiusY);
            if (ratio > 1.0) { double grow = Math.Sqrt(ratio); radiusX *= grow; radiusY *= grow; }
            double numerator = radiusX * radiusX * radiusY * radiusY - radiusX * radiusX * localY * localY - radiusY * radiusY * localX * localX;
            double denominator = radiusX * radiusX * localY * localY + radiusY * radiusY * localX * localX;
            double factor = denominator == 0.0 ? 0.0 : Math.Sqrt(Math.Max(0.0, numerator / denominator));
            if (large == sweep) factor = -factor;
            double localCenterX = factor * radiusX * localY / radiusY;
            double localCenterY = -factor * radiusY * localX / radiusX;
            double centerX = cosPhi * localCenterX - sinPhi * localCenterY + (x1 + x2) / 2.0;
            double centerY = sinPhi * localCenterX + cosPhi * localCenterY + (y1 + y2) / 2.0;
            double startVectorX = (localX - localCenterX) / radiusX;
            double startVectorY = (localY - localCenterY) / radiusY;
            double endVectorX = (-localX - localCenterX) / radiusX;
            double endVectorY = (-localY - localCenterY) / radiusY;
            double startAngle = Math.Atan2(startVectorY, startVectorX);
            double delta = Math.Atan2(startVectorX * endVectorY - startVectorY * endVectorX, startVectorX * endVectorX + startVectorY * endVectorY);
            if (!sweep && delta > 0.0) delta -= Math.PI * 2.0;
            else if (sweep && delta < 0.0) delta += Math.PI * 2.0;
            int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2.0) - 1e-9));
            double step = delta / segments, handle = 4.0 / 3.0 * Math.Tan(step / 4.0);
            double fromX = x1, fromY = y1;
            for (int i = 0; i < segments; i++)
            {
                double toAngle = startAngle + step * (i + 1), fromAngle = startAngle + step * i;
                double toX = centerX + radiusX * cosPhi * Math.Cos(toAngle) - radiusY * sinPhi * Math.Sin(toAngle);
                double toY = centerY + radiusX * sinPhi * Math.Cos(toAngle) + radiusY * cosPhi * Math.Sin(toAngle);
                double fromTangentX = -radiusX * cosPhi * Math.Sin(fromAngle) - radiusY * sinPhi * Math.Cos(fromAngle);
                double fromTangentY = -radiusX * sinPhi * Math.Sin(fromAngle) + radiusY * cosPhi * Math.Cos(fromAngle);
                double toTangentX = -radiusX * cosPhi * Math.Sin(toAngle) - radiusY * sinPhi * Math.Cos(toAngle);
                double toTangentY = -radiusX * sinPhi * Math.Sin(toAngle) + radiusY * cosPhi * Math.Cos(toAngle);
                path.AddBezier((float)fromX, (float)fromY,
                    (float)(fromX + handle * fromTangentX), (float)(fromY + handle * fromTangentY),
                    (float)(toX - handle * toTangentX), (float)(toY - handle * toTangentY), (float)toX, (float)toY);
                fromX = toX; fromY = toY;
            }
        }

        static void SkipSeparators(string data, ref int index)
        {
            while (index < data.Length && (char.IsWhiteSpace(data[index]) || data[index] == ',')) index++;
        }

        static float Number(string data, ref int index)
        {
            SkipSeparators(data, ref index);
            int start = index;
            if (index < data.Length && (data[index] == '+' || data[index] == '-')) index++;
            while (index < data.Length && char.IsDigit(data[index])) index++;
            if (index < data.Length && data[index] == '.')
            {
                index++;
                while (index < data.Length && char.IsDigit(data[index])) index++;
            }
            if (index < data.Length && (data[index] == 'e' || data[index] == 'E'))
            {
                index++;
                if (index < data.Length && (data[index] == '+' || data[index] == '-')) index++;
                while (index < data.Length && char.IsDigit(data[index])) index++;
            }
            if (index == start) throw new FormatException("SVG path data has an invalid number at index " + index.ToString(CultureInfo.InvariantCulture));
            return float.Parse(data.Substring(start, index - start), CultureInfo.InvariantCulture);
        }
    }
}
