using System.Drawing.Drawing2D;

namespace SmartHouseControl;

public sealed class TrendGraph : Control
{
    private readonly List<double> points = new();
    private readonly int capacity;
    public string Unit { get; set; } = "";
    public Color LineColor { get; set; } = Color.FromArgb(68, 189, 160);

    public TrendGraph(int capacity = 100)
    {
        this.capacity = capacity;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(27, 38, 52);
        ForeColor = Color.FromArgb(177, 196, 211);
    }

    public void Add(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        points.Add(value);
        if (points.Count > capacity) points.RemoveAt(0);
        Invalidate();
    }

    public void Clear() { points.Clear(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var chart = new Rectangle(46, 14, Math.Max(1, Width - 64), Math.Max(1, Height - 40));
        if (chart.Width < 10 || chart.Height < 10) return;

        double lo = points.Count > 0 ? points.Min() : 0;
        double hi = points.Count > 0 ? points.Max() : 100;
        if (hi - lo < 4) { lo -= 2; hi += 2; }
        else { double pad = (hi - lo) * 0.12; lo -= pad; hi += pad; }
        using var gridPen = new Pen(Color.FromArgb(57, 73, 89), 1);
        using var textBrush = new SolidBrush(ForeColor);
        using var linePen = new Pen(LineColor, 2.5f);
        using var f = new Font("Segoe UI", 8.5f);
        for (int i = 0; i <= 4; i++)
        {
            float y = chart.Top + chart.Height * i / 4f;
            g.DrawLine(gridPen, chart.Left, y, chart.Right, y);
            var label = (hi - (hi - lo) * i / 4).ToString("0.#");
            g.DrawString(label, f, textBrush, 4, y - 8);
        }
        g.DrawString("이전  ←                 최근  →", f, textBrush,
            chart.Left + Math.Max(0, chart.Width / 2 - 65), chart.Bottom + 5);
        if (points.Count < 2) return;

        var coords = new PointF[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            coords[i] = new PointF(
                chart.Left + chart.Width * i / (float)(capacity - 1),
                chart.Bottom - (float)((points[i] - lo) / (hi - lo) * chart.Height));
        }
        g.DrawLines(linePen, coords);
        var last = coords[^1];
        using var dot = new SolidBrush(LineColor);
        g.FillEllipse(dot, last.X - 4, last.Y - 4, 8, 8);
    }
}
