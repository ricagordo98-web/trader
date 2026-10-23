namespace Trader.Shared.Components;

public sealed class WorkspacePanel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Symbol { get; set; } = string.Empty;

    public int Timeframe { get; set; } = 4;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 560;

    public double Height { get; set; } = 380;

    public int Z { get; set; }
}

public static class WorkspaceLayout
{
    public const double Gap = 12;
    public const double MinWidth = 320;
    public const double MinHeight = 220;
    public const double CascadeStep = 32;
    public const double CascadeWidth = 640;
    public const double CascadeHeight = 420;
    public const double SnapSize = 10;

    public static int AutoColumns(int count) => count switch
    {
        <= 1 => 1,
        <= 4 => 2,
        _ => 3
    };

    public static void ArrangeGrid(IReadOnlyList<WorkspacePanel> panels, double width, double height, int columns)
    {
        if (panels.Count == 0)
        {
            return;
        }

        int cols = Math.Clamp(columns, 1, Math.Max(1, panels.Count));
        int rows = (int)Math.Ceiling(panels.Count / (double)cols);
        double panelWidth = Math.Max(MinWidth, (width - Gap * (cols + 1)) / cols);
        double panelHeight = Math.Max(MinHeight + 100, (height - Gap * (rows + 1)) / rows);

        for (int i = 0; i < panels.Count; i++)
        {
            WorkspacePanel panel = panels[i];
            panel.X = Gap + i % cols * (panelWidth + Gap);
            panel.Y = Gap + i / cols * (panelHeight + Gap);
            panel.Width = Math.Floor(panelWidth);
            panel.Height = Math.Floor(panelHeight);
        }
    }

    public static void ArrangeCascade(IReadOnlyList<WorkspacePanel> panels)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            panels[i].X = Gap + i * CascadeStep;
            panels[i].Y = Gap + i * CascadeStep;
            panels[i].Width = CascadeWidth;
            panels[i].Height = CascadeHeight;
        }
    }

    public static void PlaceNew(WorkspacePanel panel, IReadOnlyList<WorkspacePanel> existing)
    {
        int offset = existing.Count % 8;
        panel.X = Gap + offset * CascadeStep;
        panel.Y = Gap + offset * CascadeStep;
        panel.Width = CascadeWidth;
        panel.Height = CascadeHeight;
    }

    public static double ContentHeight(IReadOnlyList<WorkspacePanel> panels, double viewportHeight) =>
        Math.Max(viewportHeight, panels.Count == 0 ? 0 : panels.Max(p => p.Y + p.Height) + Gap * 2);

    public static void Normalize(WorkspacePanel panel)
    {
        panel.X = Math.Max(0, panel.X);
        panel.Y = Math.Max(0, panel.Y);
        panel.Width = Math.Max(MinWidth, panel.Width);
        panel.Height = Math.Max(MinHeight, panel.Height);
    }
}
