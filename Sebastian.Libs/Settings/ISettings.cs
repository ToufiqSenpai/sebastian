namespace Sebastian.Libs.Settings;

public interface ISettings
{
    bool Has(string key);

    string? Read(string key);

    void Write(string key, string value);

    void Remove(string key);
}
