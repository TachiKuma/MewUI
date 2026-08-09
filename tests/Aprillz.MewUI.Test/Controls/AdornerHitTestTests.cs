using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// An adorner spans the whole element it adorns, so it must answer the pointer only where it draws.
/// Answering for the empty space around its children puts a sheet of glass over the content below,
/// which reads as a window that stopped responding.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AdornerHitTestTests
{
    [TestMethod]
    public void TheSpaceAroundAnAdornerFallsThroughToTheContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 200);
        var content = new Button().Content("under");
        window.Content = content;
        window.PerformLayout();

        var badge = new Border { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        var adorner = new Adorner(content, badge);
        AdornerLayer.GetAdornerLayer(content)!.Add(adorner);
        window.PerformLayout();

        var onBadge = new Point(badge.Bounds.X + badge.Bounds.Width / 2, badge.Bounds.Y + badge.Bounds.Height / 2);
        Assert.AreSame(badge, window.HitTest(onBadge), "the adorner's own content should take the pointer");

        var besideBadge = new Point(badge.Bounds.X - 40, badge.Bounds.Bottom + 40);
        var hit = window.HitTest(besideBadge);

        Assert.IsNotNull(hit, "the pointer landed on nothing where the content should have been");
        Assert.AreNotSame(adorner, hit, "the adorner answered for space it does not draw in");
    }

    [TestMethod]
    public void AThemeChangeReachesEverythingAnAdornerCarries()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 200);
        var content = new Border();
        window.Content = content;
        window.PerformLayout();

        int themedDepth = 0;
        var deep = new Border();
        deep.WithTheme((_, _) => themedDepth++);
        var badge = new Border { Child = deep };
        AdornerLayer.GetAdornerLayer(content)!.Add(new Adorner(content, badge));
        window.PerformLayout();

        int before = themedDepth;
        var theme = window.ThemeInternal;
        window.BroadcastThemeChanged(theme, theme);

        Assert.IsGreaterThan(before, themedDepth,
            "the theme change stopped at the adorner and never reached what it carries");
    }
}
