using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MultiSelectTests
{
    static MainViewModel MakeVmWithTwoLayers(out SlideLayer a, out SlideLayer b)
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg  = show.Packages.Last();
        var page = new Page();
        a = new SlideLayer { Type = LayerType.Text, Name = "A", ZOrder = 1 };
        b = new SlideLayer { Type = LayerType.Text, Name = "B", ZOrder = 2 };
        page.AddLayer(a);
        page.AddLayer(b);
        pkg.AddPage(page);
        vm.OpenEditor(new PageViewModel(page, pkg));
        return vm;
    }

    [Fact]
    public void SelectedLayer_set_SyncsSelectedLayersToSingleton()
    {
        var vm = MakeVmWithTwoLayers(out var a, out _);
        vm.SelectedLayer = a;
        Assert.Single(vm.SelectedLayers);
        Assert.Contains(a, vm.SelectedLayers);
    }

    [Fact]
    public void SelectedLayer_setNull_ClearsSelectedLayers()
    {
        var vm = MakeVmWithTwoLayers(out var a, out _);
        vm.SelectedLayer = a;
        vm.SelectedLayer = null;
        Assert.Empty(vm.SelectedLayers);
    }

    [Fact]
    public void SetMultiSelection_SetsCollectionAndPicksTopmostAsPrimary()
    {
        var vm = MakeVmWithTwoLayers(out var a, out var b);
        // b has ZOrder=2 (higher), a has ZOrder=1
        vm.SetMultiSelection(new[] { a, b });
        Assert.Equal(2, vm.SelectedLayers.Count);
        Assert.Contains(a, vm.SelectedLayers);
        Assert.Contains(b, vm.SelectedLayers);
        Assert.Equal(b, vm.SelectedLayer); // topmost by ZOrder
    }

    [Fact]
    public void DeleteSelectedLayers_RemovesAllFromPage()
    {
        var vm = MakeVmWithTwoLayers(out var a, out var b);
        vm.SetMultiSelection(new[] { a, b });
        vm.DeleteSelectedLayers();
        Assert.Empty(vm.EditingPage!.Layers);
        Assert.Null(vm.SelectedLayer);
        Assert.Empty(vm.SelectedLayers);
    }

    [Fact]
    public void ToggleVisibilityForSelected_TogglesAllSelectedLayers()
    {
        var vm = MakeVmWithTwoLayers(out var a, out var b);
        a.Visible = true; b.Visible = true;
        vm.SetMultiSelection(new[] { a, b });
        vm.ToggleVisibilityForSelected();
        Assert.False(a.Visible);
        Assert.False(b.Visible);
        // Selection should be preserved
        Assert.Equal(2, vm.SelectedLayers.Count);
    }
}
