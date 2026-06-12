using System.Collections.Generic;
using System.Linq;
using AudioShare.Models;

namespace AudioShare.Services;

public interface IGroupService
{
    IReadOnlyList<DeviceGroup> Groups { get; }
    DeviceGroup Create(string name);
    void Delete(string id);
    void Rename(string id, string name);
    void Save(DeviceGroup group);
}

public class GroupService : IGroupService
{
    private readonly ISettingsService _settings;

    public GroupService(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<DeviceGroup> Groups => _settings.Current.Groups;

    public DeviceGroup Create(string name)
    {
        var group = new DeviceGroup { Name = name };
        _settings.Current.Groups.Add(group);
        _settings.Save();
        return group;
    }

    public void Delete(string id)
    {
        var existing = _settings.Current.Groups.FirstOrDefault(g => g.Id == id);
        if (existing is null) return;
        _settings.Current.Groups.Remove(existing);
        _settings.Save();
    }

    public void Rename(string id, string name)
    {
        var existing = _settings.Current.Groups.FirstOrDefault(g => g.Id == id);
        if (existing is null) return;
        existing.Name = name;
        _settings.Save();
    }

    public void Save(DeviceGroup group)
    {
        var existing = _settings.Current.Groups.FirstOrDefault(g => g.Id == group.Id);
        if (existing is null)
        {
            _settings.Current.Groups.Add(group);
        }
        else
        {
            existing.Name = group.Name;
            existing.DeviceIds = group.DeviceIds;
            existing.ManualDelaysMs = group.ManualDelaysMs;
        }
        _settings.Save();
    }
}
