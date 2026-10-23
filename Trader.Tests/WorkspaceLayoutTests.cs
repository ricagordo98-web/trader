using FluentAssertions;
using Trader.Shared.Components;

namespace Trader.Tests;

public class WorkspaceLayoutTests
{
    private static List<WorkspacePanel> Panels(int count) =>
        Enumerable.Range(0, count).Select(i => new WorkspacePanel { Symbol = $"S{i}" }).ToList();

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(9, 3)]
    public void AutoColumns_GrowsWithThePanelCount(int count, int expected) =>
        WorkspaceLayout.AutoColumns(count).Should().Be(expected);

    [Fact]
    public void ArrangeGrid_FourPanelsInTwoColumns_FillsTheWorkspaceWithoutOverlap()
    {
        List<WorkspacePanel> panels = Panels(4);

        WorkspaceLayout.ArrangeGrid(panels, 1212, 812, 2);

        panels.Select(p => (p.X, p.Y)).Distinct().Should().HaveCount(4);
        panels.Should().OnlyContain(p => p.Width >= WorkspaceLayout.MinWidth && p.Height >= WorkspaceLayout.MinHeight);
        panels[0].X.Should().Be(WorkspaceLayout.Gap);
        panels[1].X.Should().BeGreaterThan(panels[0].X + panels[0].Width);
        panels[2].Y.Should().BeGreaterThan(panels[0].Y + panels[0].Height);
        panels.Max(p => p.X + p.Width).Should().BeLessThanOrEqualTo(1212);
    }

    [Fact]
    public void ArrangeGrid_MoreColumnsThanPanels_UsesOnlyAsManyAsNeeded()
    {
        List<WorkspacePanel> panels = Panels(2);

        WorkspaceLayout.ArrangeGrid(panels, 1200, 800, 3);

        panels[0].Y.Should().Be(panels[1].Y);
        panels[0].Width.Should().BeGreaterThan(500);
    }

    [Fact]
    public void ArrangeGrid_TinyWorkspace_KeepsMinimumSizes()
    {
        List<WorkspacePanel> panels = Panels(6);

        WorkspaceLayout.ArrangeGrid(panels, 400, 300, 3);

        panels.Should().OnlyContain(p => p.Width >= WorkspaceLayout.MinWidth && p.Height >= WorkspaceLayout.MinHeight);
    }

    [Fact]
    public void ArrangeCascade_OffsetsEachPanel()
    {
        List<WorkspacePanel> panels = Panels(3);

        WorkspaceLayout.ArrangeCascade(panels);

        panels[1].X.Should().Be(panels[0].X + WorkspaceLayout.CascadeStep);
        panels[2].Y.Should().Be(panels[0].Y + 2 * WorkspaceLayout.CascadeStep);
    }

    [Fact]
    public void ContentHeight_GrowsToFitPanelsDraggedBelowTheViewport()
    {
        List<WorkspacePanel> panels = Panels(1);
        panels[0].Y = 900;
        panels[0].Height = 400;

        WorkspaceLayout.ContentHeight(panels, 700).Should().BeGreaterThan(1300);
        WorkspaceLayout.ContentHeight([], 700).Should().Be(700);
    }

    [Fact]
    public void Normalize_ClampsNegativePositionsAndTinySizes()
    {
        WorkspacePanel panel = new() { X = -20, Y = -5, Width = 100, Height = 50 };

        WorkspaceLayout.Normalize(panel);

        (panel.X, panel.Y, panel.Width, panel.Height).Should().Be((0, 0, WorkspaceLayout.MinWidth, WorkspaceLayout.MinHeight));
    }
}
