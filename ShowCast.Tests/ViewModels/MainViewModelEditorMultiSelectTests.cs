using System.Linq;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelEditorMultiSelectTests
{
    // Creates a 4-page package: page[0] is AddPackageToShow's default page,
    // pages[1..3] are added manually with names "2"/"3"/"4".
    // OpenEditor rebuilds EditorPages → ep0..ep3.
    static (MainViewModel vm, Package pkg,
            PageViewModel ep0, PageViewModel ep1,
            PageViewModel ep2, PageViewModel ep3) Setup()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();

        pkg.AddPage(new Page { Name = "2" });
        pkg.AddPage(new Page { Name = "3" });
        pkg.AddPage(new Page { Name = "4" });

        var pvm0 = new PageViewModel(pkg.Pages[0], pkg);
        vm.OpenEditor(pvm0);

        return (vm, pkg,
                vm.EditorPages[0], vm.EditorPages[1],
                vm.EditorPages[2], vm.EditorPages[3]);
    }

    [Fact]
    public void SetEditorPageSelection_SetsSelectedEditorPages()
    {
        var (vm, _, _, ep1, ep2, _) = Setup();

        vm.SetEditorPageSelection(new[] { ep1, ep2 });

        Assert.Equal(2, vm.SelectedEditorPages.Count);
        Assert.Contains(ep1, vm.SelectedEditorPages);
        Assert.Contains(ep2, vm.SelectedEditorPages);
    }

    [Fact]
    public void RemoveSelectedEditorPages_RemovesPagesAndNavigatesToAdjacent()
    {
        var (vm, pkg, _, ep1, ep2, _) = Setup();
        vm.SetEditorPageSelection(new[] { ep1, ep2 });

        vm.RemoveSelectedEditorPages();

        Assert.Equal(2, pkg.Pages.Count);
        Assert.DoesNotContain(ep1.Model, pkg.Pages);
        Assert.DoesNotContain(ep2.Model, pkg.Pages);
        Assert.Equal(2, vm.EditorPages.Count);
        Assert.NotNull(vm.EditingPage);
    }

    [Fact]
    public void RemoveSelectedEditorPages_WhenAllPagesRemoved_ClosesEditor()
    {
        // Needs a 1-page package; Setup() provides 4 pages, so set up inline.
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg  = show.Packages.Last();
        var pvm0 = new PageViewModel(pkg.Pages[0], pkg);
        vm.OpenEditor(pvm0);

        vm.SetEditorPageSelection(new[] { vm.EditorPages[0] });
        vm.RemoveSelectedEditorPages();

        Assert.False(vm.IsEditorOpen);
    }

    [Fact]
    public void DuplicateSelectedEditorPages_InsertsGroupAfterLastSelected()
    {
        // Setup: pages [0,1,2,3] (ep0..ep3). Select ep1 and ep2.
        // After duplicate: [ep0, ep1, ep2, copy-of-ep1, copy-of-ep2, ep3]
        var (vm, pkg, ep0, ep1, ep2, ep3) = Setup();
        vm.SetEditorPageSelection(new[] { ep1, ep2 });

        vm.DuplicateSelectedEditorPages();

        Assert.Equal(6, pkg.Pages.Count);
        Assert.Equal(6, vm.EditorPages.Count);

        // ep0, ep1, ep2 stay at their original positions
        Assert.Equal(ep0.Model, pkg.Pages[0]);
        Assert.Equal(ep1.Model, pkg.Pages[1]);
        Assert.Equal(ep2.Model, pkg.Pages[2]);

        // Copies are at indices 3 and 4; ep3 is pushed to index 5
        Assert.Equal(ep3.Model, pkg.Pages[5]);

        // EditorPages[3] and [4] are the new copies; their Models
        // are distinct from the originals (Clone creates new Page objects)
        Assert.NotSame(ep1.Model, vm.EditorPages[3].Model);
        Assert.NotSame(ep2.Model, vm.EditorPages[4].Model);
        Assert.Same(ep3,          vm.EditorPages[5]);
    }

    [Fact]
    public void MovePages_MovesGroupJustBeforeTarget()
    {
        // Setup: [ep0, ep1, ep2, ep3]. Move ep0 and ep1 to just before ep3.
        // MovePage semantics: drop target = insert just before that target.
        // Expected result: [ep2, ep0, ep1, ep3]
        var (vm, pkg, ep0, ep1, ep2, ep3) = Setup();

        vm.MovePages(new[] { ep0, ep1 }, ep3);

        Assert.Equal(ep2.Model, pkg.Pages[0]);
        Assert.Equal(ep0.Model, pkg.Pages[1]);
        Assert.Equal(ep1.Model, pkg.Pages[2]);
        Assert.Equal(ep3.Model, pkg.Pages[3]);
    }
}
