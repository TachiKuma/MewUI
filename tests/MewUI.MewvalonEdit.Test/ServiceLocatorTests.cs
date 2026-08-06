using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.MewvalonEdit.Rendering;

namespace Aprillz.MewUI.MewvalonEdit.Test;

/// <summary>
/// The four-step lookup ported code relies on: editor to text area to view services to document
/// services. Ported code holds whichever of the three it was handed and still finds the rest.
/// </summary>
[TestClass]
public sealed class ServiceLocatorTests
{
    [TestMethod]
    public void EachComponentIsRegisteredOnTheView()
    {
        var editor = new TextEditor();

        Assert.AreSame(editor, ((IServiceProvider)editor).GetService(typeof(TextEditor)));
        Assert.AreSame(editor.TextArea, editor.TextArea.GetService(typeof(TextArea)));
        Assert.AreSame(editor.TextArea.TextView, editor.TextArea.TextView.GetService(typeof(TextView)));
    }

    [TestMethod]
    public void AnUnregisteredServiceIsNull()
    {
        var editor = new TextEditor();

        Assert.IsNull(((IServiceProvider)editor).GetService(typeof(FoldingManager)));
    }

    /// <summary>
    /// A service put on the document is reached through a view of it, which is why the view's own
    /// container is not the whole lookup.
    /// </summary>
    [TestMethod]
    public void DocumentServicesAreReachedThroughTheView()
    {
        var editor = new TextEditor();
        var probe = new object();
        ((ServiceContainer)editor.Document.ServiceProvider).AddService(typeof(object), probe);

        Assert.IsNull(editor.TextArea.TextView.Services.GetService(typeof(object)));
        Assert.AreSame(probe, editor.TextArea.TextView.GetService(typeof(object)));
        Assert.AreSame(probe, ((IServiceProvider)editor).GetService(typeof(object)));
    }

    [TestMethod]
    public void ADocumentCarriesItself()
    {
        var document = new TextDocument("text");

        Assert.AreSame(document, ((IServiceProvider)document).GetService(typeof(TextDocument)));
    }

    [TestMethod]
    public void ReplacingTheServiceProviderReplacesTheWholeContainer()
    {
        var document = new TextDocument("text");
        var container = new ServiceContainer();

        document.ServiceProvider = container;

        Assert.IsNull(((IServiceProvider)document).GetService(typeof(TextDocument)));
        Assert.ThrowsExactly<ArgumentNullException>(() => document.ServiceProvider = null!);
    }

    /// <summary>A folding manager registers on install and is gone after uninstall.</summary>
    [TestMethod]
    public void TheFoldingManagerIsRegisteredWhileInstalled()
    {
        var editor = new TextEditor();

        var manager = FoldingManager.Install(editor);
        Assert.AreSame(manager, editor.TextArea.GetService(typeof(FoldingManager)));

        FoldingManager.Uninstall(manager);
        Assert.IsNull(editor.TextArea.GetService(typeof(FoldingManager)));
    }
}
