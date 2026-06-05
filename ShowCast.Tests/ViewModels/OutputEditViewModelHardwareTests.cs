using System;
using System.Collections.Generic;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class OutputEditViewModelHardwareTests
{
    static int BlackmagicIndex => Array.IndexOf(OutputEditViewModel.TypeLabels, "Blackmagic");
    static int AjaIndex        => Array.IndexOf(OutputEditViewModel.TypeLabels, "AJA");

    [Fact]
    public void TypeLabels_ContainsBlackmagicAndAja()
    {
        Assert.Contains("Blackmagic", OutputEditViewModel.TypeLabels);
        Assert.Contains("AJA",        OutputEditViewModel.TypeLabels);
    }

    [Fact]
    public void IsBlackmagic_TrueWhenTypeIndexIsBlackmagic()
    {
        var vm = new OutputEditViewModel();
        vm.TypeIndex = BlackmagicIndex;
        Assert.True(vm.IsBlackmagic);
        Assert.False(vm.IsAja);
        Assert.False(vm.IsDisplay);
        Assert.False(vm.IsNDI);
    }

    [Fact]
    public void IsAja_TrueWhenTypeIndexIsAja()
    {
        var vm = new OutputEditViewModel();
        vm.TypeIndex = AjaIndex;
        Assert.True(vm.IsAja);
        Assert.False(vm.IsBlackmagic);
    }

    [Fact]
    public void WriteTo_BlackmagicType_SetsDeviceSerialFromList()
    {
        var vm = new OutputEditViewModel();
        vm.TypeIndex = BlackmagicIndex;
        vm.AvailableHardwareDevices = new List<string> { "DeckLink 8K Pro", "DeckLink Duo 2" };
        vm.HardwareDeviceIndex = 1;
        var cfg = new OutputConfig();
        vm.WriteTo(cfg);
        Assert.Equal(OutputType.Blackmagic, cfg.Type);
        Assert.Equal("DeckLink Duo 2", cfg.DeviceSerial);
    }

    [Fact]
    public void LoadFrom_BlackmagicType_SetsHardwareDeviceIndexBySerial()
    {
        var vm = new OutputEditViewModel();
        vm.AvailableHardwareDevices = new List<string> { "DeckLink 8K Pro", "DeckLink Duo 2" };
        var cfg = new OutputConfig { Type = OutputType.Blackmagic, DeviceSerial = "DeckLink Duo 2" };
        vm.LoadFrom(cfg, 1);
        Assert.Equal(BlackmagicIndex, vm.TypeIndex);
        Assert.Equal(1, vm.HardwareDeviceIndex);
    }

    [Fact]
    public void LoadFrom_BlackmagicType_UnknownSerial_FallsBackToIndex0()
    {
        var vm = new OutputEditViewModel();
        vm.AvailableHardwareDevices = new List<string> { "DeckLink 8K Pro" };
        var cfg = new OutputConfig { Type = OutputType.Blackmagic, DeviceSerial = "NonExistent" };
        vm.LoadFrom(cfg, 1);
        Assert.Equal(0, vm.HardwareDeviceIndex);
    }
}
