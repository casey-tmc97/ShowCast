using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class DuplicateLayerTests
{
    [Fact]
    public void DuplicateLayer_AddsLayerWithOffsetAndNewId()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg  = show.Packages.Last();

        var page  = new Page();
        var layer = new SlideLayer { Type = LayerType.Text, Name = "Original", X = 0.1f, Y = 0.2f };
        page.AddLayer(layer);
        pkg.AddPage(page);

        vm.OpenEditor(new PageViewModel(page, pkg));
        vm.SelectedLayer = layer;

        int countBefore = vm.EditingPage!.Layers.Count;
        vm.DuplicateLayer(layer);

        Assert.Equal(countBefore + 1, vm.EditingPage.Layers.Count);
        Assert.NotEqual(layer.Id, vm.SelectedLayer!.Id);
        Assert.Equal("Original Copy", vm.SelectedLayer.Name);
        Assert.True(vm.SelectedLayer.X > layer.X);  // offset applied
    }
}
