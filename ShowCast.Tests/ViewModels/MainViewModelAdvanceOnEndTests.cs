using System;
using System.Linq;
using System.Reflection;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelAdvanceOnEndTests
{
    static void CallHandleVideoEnded(MainViewModel vm, OutputState output)
    {
        var method = typeof(MainViewModel).GetMethod(
            "HandleVideoEnded",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("HandleVideoEnded not found");
        method.Invoke(vm, new object[] { output });
    }

    // ── Flat-view tests ────────────────────────────────────────────────────────

    [Fact]
    public void HandleVideoEnded_FlatView_AdvancesToNextPage()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();
        pkg.AddPage(new Page { Name = "2" });
        vm.LoadPackageToSelectedOutput(pkg);  // reload so vm.Pages has both pages
        vm.SelectedPage = vm.Pages[0];
        vm.GoLive();

        var output = vm.SelectedOutput!;
        Assert.Equal(pkg.Pages[0], output.LivePage);

        CallHandleVideoEnded(vm, output);

        Assert.Equal(pkg.Pages[1], output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_FlatView_LastPage_GoesBlack()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();
        pkg.AddPage(new Page { Name = "2" });
        vm.LoadPackageToSelectedOutput(pkg);
        vm.SelectedPage = vm.Pages[1];        // last page
        vm.GoLive();

        var output = vm.SelectedOutput!;
        CallHandleVideoEnded(vm, output);

        Assert.Null(output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_FlatView_NoLivePage_IsNoOp()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();
        vm.LoadPackageToSelectedOutput(pkg);
        // No GoLive — output.LivePage is null

        var output = vm.SelectedOutput!;
        CallHandleVideoEnded(vm, output);     // must not throw

        Assert.Null(output.LivePage);
    }

    // ── Rundown-view tests ─────────────────────────────────────────────────────

    [Fact]
    public void HandleVideoEnded_RundownView_AdvancesToNextPageInGroup()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();
        pkg.AddPage(new Page { Name = "2" });  // add before SelectedRundown assignment
        var rd = vm.AddRundown("RD");
        rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
        vm.SelectedRundown = rd;               // triggers RefreshPackageItems → RefreshPageGroups

        var group = vm.PageGroups[0];
        vm.GoLiveFromGroup(group.Pages[0]);

        var output = group.SelectedOutput!;
        Assert.Equal(pkg.Pages[0], output.LivePage);

        CallHandleVideoEnded(vm, output);

        Assert.Equal(pkg.Pages[1], output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_RundownView_LastPage_GoesBlack()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();
        pkg.AddPage(new Page { Name = "2" });
        var rd = vm.AddRundown("RD");
        rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
        vm.SelectedRundown = rd;

        var group = vm.PageGroups[0];
        vm.GoLiveFromGroup(group.Pages[1]);    // last page

        var output = group.SelectedOutput!;
        CallHandleVideoEnded(vm, output);

        Assert.Null(output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_RundownView_UnknownOutput_IsNoOp()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();
        var rd = vm.AddRundown("RD");
        rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
        vm.SelectedRundown = rd;

        // An output not tied to any group
        var orphan = new OutputState(new OutputConfig { Name = "Orphan" });
        orphan.GoLive(pkg.Pages[0], 0);

        CallHandleVideoEnded(vm, orphan);    // must not throw
    }
}
