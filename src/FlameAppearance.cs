using System;

namespace PBZKeyForge
{
    public sealed class FlameAppearance
    {
        public bool Enabled = true;
        public int Speed = 75;
        public int Height = 30;
        public int Brightness = 100;
        public int Transparency = 15;
        public int Hue;

        public FlameAppearance Clone() { return (FlameAppearance)MemberwiseClone(); }

        internal void Clamp()
        {
            Speed = Math.Max(5, Math.Min(300, Speed));
            Height = Math.Max(5, Math.Min(100, Height));
            Brightness = Math.Max(0, Math.Min(300, Brightness));
            Transparency = Math.Max(0, Math.Min(100, Transparency));
            Hue = Math.Max(-180, Math.Min(180, Hue));
        }
    }
}
