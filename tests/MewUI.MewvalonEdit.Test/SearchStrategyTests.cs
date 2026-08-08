using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Search;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The search algorithm sits behind an interface, so the panel's options are one way of choosing it
/// and a caller can supply another.
/// </summary>
[TestClass]
public sealed class SearchStrategyTests
{
    private const string TEXT = "cat category cat. CAT";

    private static ITextSource Source(string text) => new TextDocument(text);

    [TestMethod]
    public void ALiteralPatternMatchesItselfAndNothingElse()
    {
        var strategy = SearchStrategyFactory.Create("cat", ignoreCase: false, matchWholeWords: false, SearchMode.Normal);

        var results = strategy.FindAll(Source(TEXT), 0, TEXT.Length).ToArray();

        Assert.HasCount(3, results);
        Assert.AreEqual(0, results[0].Offset);
    }

    [TestMethod]
    public void WholeWordsRejectsAMatchInsideAWord()
    {
        var strategy = SearchStrategyFactory.Create("cat", ignoreCase: false, matchWholeWords: true, SearchMode.Normal);

        var results = strategy.FindAll(Source(TEXT), 0, TEXT.Length).ToArray();

        // "category" is out; "cat" and "cat." remain.
        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void IgnoringCaseReachesTheUppercaseMatch()
    {
        var strategy = SearchStrategyFactory.Create("cat", ignoreCase: true, matchWholeWords: true, SearchMode.Normal);

        Assert.HasCount(3, strategy.FindAll(Source(TEXT), 0, TEXT.Length).ToArray());
    }

    [TestMethod]
    public void AWildcardStandsForAnyRun()
    {
        var strategy = SearchStrategyFactory.Create("c*y", ignoreCase: false, matchWholeWords: false, SearchMode.Wildcard);

        var results = strategy.FindAll(Source(TEXT), 0, TEXT.Length).ToArray();

        Assert.HasCount(1, results);
        Assert.AreEqual("cat category", TEXT.Substring(results[0].Offset, results[0].Length));
    }

    /// <summary>A literal pattern is escaped, so regular expression syntax in it is just text.</summary>
    [TestMethod]
    public void ALiteralPatternDoesNotReadAsAnExpression()
    {
        var strategy = SearchStrategyFactory.Create("c.t", ignoreCase: false, matchWholeWords: false, SearchMode.Normal);

        Assert.IsEmpty(strategy.FindAll(Source(TEXT), 0, TEXT.Length).ToArray());
    }

    [TestMethod]
    public void AnUnusablePatternIsReportedAsSuch()
        => Assert.ThrowsExactly<SearchPatternException>(
            () => SearchStrategyFactory.Create("(unclosed", ignoreCase: false, matchWholeWords: false, SearchMode.RegEx));

    [TestMethod]
    public void AReplacementReadsItsGroupsFromTheMatch()
    {
        var strategy = SearchStrategyFactory.Create(@"(\w+)@(\w+)", ignoreCase: false, matchWholeWords: false, SearchMode.RegEx);

        var result = strategy.FindNext(Source("mail user@host end"), 0, 18);

        Assert.IsNotNull(result);
        Assert.AreEqual("host/user", result.ReplaceWith("$2/$1"));
    }

    /// <summary>The panel's options pick a strategy, and a caller can replace the whole thing.</summary>
    [TestMethod]
    public void ThePanelUsesTheStrategyItWasGiven()
    {
        var editor = new TextEditor { Text = TEXT };
        var panel = SearchPanel.Install(editor);

        panel.SearchPattern = "cat";
        panel.MatchCase = true;
        panel.WholeWords = true;
        Assert.HasCount(2, panel.Results);

        panel.SearchMode = SearchMode.Wildcard;
        panel.SearchPattern = "c*y";
        panel.WholeWords = false;
        Assert.HasCount(1, panel.Results);

        panel.SearchStrategy = SearchStrategyFactory.Create(
            "CAT", ignoreCase: false, matchWholeWords: false, SearchMode.Normal);
        Assert.HasCount(1, panel.Results);
        Assert.AreEqual(TEXT.IndexOf("CAT", StringComparison.Ordinal), panel.Results[0].Offset);

        panel.Uninstall();
    }

    [TestMethod]
    public void FindPreviousWalksBackwardsAndWraps()
    {
        var editor = new TextEditor { Text = TEXT };
        var panel = SearchPanel.Install(editor);
        panel.SearchPattern = "cat";

        var last = panel.FindPrevious(0);
        Assert.IsNotNull(last);
        Assert.AreEqual(panel.Results[^1].Offset, last.Value.Offset, "Searching back from the start must wrap.");

        var previous = panel.FindPrevious(panel.Results[1].Offset);
        Assert.IsNotNull(previous);
        Assert.AreEqual(panel.Results[0].Offset, previous.Value.Offset);

        panel.Uninstall();
    }
}
