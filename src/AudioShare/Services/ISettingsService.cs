using AudioShare.Models;

namespace AudioShare.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Load();
    void Save();
}
