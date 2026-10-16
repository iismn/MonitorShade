using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
internal static class CreateIcon
{
    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
    private static void Monitor(Graphics g, RectangleF rect, Color color)
    {
        using (GraphicsPath p = Rounded(rect, 12)) using (Brush b = new SolidBrush(Color.FromArgb(245,248,255))) g.FillPath(b,p);
        RectangleF screen = RectangleF.Inflate(rect,-10,-10); screen.Height -= 8;
        using (GraphicsPath p = Rounded(screen,5)) using (LinearGradientBrush b = new LinearGradientBrush(screen, color, Color.FromArgb(0,75,160), 60f)) g.FillPath(b,p);
    }
    private static void Main(string[] args)
    {
        int[] sizes = {16,24,32,48,64,256}; byte[][] images = new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(Bitmap bmp = new Bitmap(sizes[i],sizes[i],PixelFormat.Format32bppArgb))
        {
            using(Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent); g.ScaleTransform(sizes[i]/256f,sizes[i]/256f);
                using(Brush shadow = new SolidBrush(Color.FromArgb(120,20,30,50))) using(GraphicsPath p = Rounded(new RectangleF(72,35,167,127),14)) g.FillPath(shadow,p);
                Monitor(g,new RectangleF(66,28,168,126),Color.FromArgb(80,200,255));
                using(Brush shadow = new SolidBrush(Color.FromArgb(120,20,30,50))) using(GraphicsPath p = Rounded(new RectangleF(24,83,180,135),14)) g.FillPath(shadow,p);
                Monitor(g,new RectangleF(18,76,180,132),Color.FromArgb(70,180,255));
                using(Brush b = new SolidBrush(Color.FromArgb(238,244,252)))
                {
                    g.FillRectangle(b,98,207,20,17);
                    using(GraphicsPath p = Rounded(new RectangleF(69,222,78,12),5)) g.FillPath(b,p);
                }
            }
            using(MemoryStream stream = new MemoryStream()) { bmp.Save(stream,ImageFormat.Png); images[i]=stream.ToArray(); }
        }
        using(BinaryWriter output = new BinaryWriter(File.Create(args[0])))
        {
            output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for(int i=0;i<sizes.Length;i++)
            {
                output.Write((byte)(sizes[i]==256?0:sizes[i])); output.Write((byte)(sizes[i]==256?0:sizes[i])); output.Write((byte)0); output.Write((byte)0);
                output.Write((ushort)1); output.Write((ushort)32); output.Write(images[i].Length); output.Write(offset); offset+=images[i].Length;
            }
            foreach(byte[] bytes in images) output.Write(bytes);
        }
    }
}
