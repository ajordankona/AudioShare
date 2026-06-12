using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AudioShare.Models;

public partial class DeviceGroup : ObservableObject
{
    [ObservableProperty]
    private string _id = System.Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = "New Group";

    public List<string> DeviceIds { get; set; } = new();

    public Dictionary<string, int> ManualDelaysMs { get; set; } = new();
}
