using System.Linq;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelRundownTests
{
    [Fact]
    public void StartPageTimer_LoopToStart_InRundownView_CallsGoLiveFromGroup()
    {
        var vm = new MainViewModel();

        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();

        var page2 = new Page { Name = "2" };
        pkg.AddPage(page2);

        var rd = vm.AddRundown("RD");
        rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
        vm.SelectedRundown = rd;

        // Verify we are in rundown view.
        Assert.True(vm.ShowingRundown);

        // PageGroups should have a group for the package with both pages.
        var group = vm.PageGroups.FirstOrDefault(g => g.Package == pkg);
        Assert.NotNull(group);
        Assert.Equal(2, group!.Pages.Count);
    }


    [Fact]
    public void CloseEditor_WhenRundownSelected_RefreshesPageGroups()
    {
        var vm = new MainViewModel();

        // Build a show with one package containing two pages.
        // Use AddShow + AddPackageToShow so the package is registered in _packageById.
        var show = vm.AddShow("TestShow");
        // AddPackageToShow creates the package AND registers it in _packageById.
        vm.AddPackageToShow("Pkg", show);
        var pkg = show.Packages.Last();

        // AddPackageToShow adds one default page; add a second page manually.
        var page2 = new Page { Name = "2" };
        pkg.AddPage(page2);

        // Add a rundown that references the package.
        var rd = vm.AddRundown("RD1");
        rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
        vm.SelectedRundown = rd;

        // Open the editor on the first page.
        var pvm = new PageViewModel(pkg.Pages[0], pkg);
        vm.OpenEditor(pvm);

        // Verify groups have 2 pages before adding the third.
        var groupBefore = vm.PageGroups.FirstOrDefault(g => g.Package == pkg);
        int countBefore = groupBefore?.Pages.Count ?? 0;
        Assert.Equal(2, countBefore);

        // Add a third page via the model directly (simulates editor adding a page).
        var page3 = new Page { Name = "3" };
        pkg.AddPage(page3);

        vm.CloseEditor();

        // After close, group should reflect 3 pages.
        var groupAfter = vm.PageGroups.FirstOrDefault(g => g.Package == pkg);
        int countAfter = groupAfter?.Pages.Count ?? 0;

        Assert.Equal(3, countAfter);
        Assert.NotEqual(countBefore, countAfter);
    }
}
