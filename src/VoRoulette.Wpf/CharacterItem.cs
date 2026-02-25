using System.ComponentModel;
using System.Runtime.CompilerServices;
using VoRoulette.Core;

namespace VoRoulette.Wpf;

public sealed class CharacterItem : INotifyPropertyChanged
{
    private bool _enabled;
    private string? _imagePath;
    private string _name;
    private double _weight;

    public CharacterItem(string name, double weight = 1, bool enabled = true, string? imagePath = null)
    {
        _name = name;
        _weight = RouletteEngine.ClampWeight(weight);
        _enabled = enabled;
        _imagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath.Trim();
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            OnPropertyChanged();
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public double Weight
    {
        get => _weight;
        set
        {
            var next = RouletteEngine.ClampWeight(value);
            if (_weight == next)
            {
                return;
            }

            _weight = next;
            OnPropertyChanged();
        }
    }

    public string? ImagePath
    {
        get => _imagePath;
        set
        {
            var next = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_imagePath == next)
            {
                return;
            }

            _imagePath = next;
            OnPropertyChanged();
        }
    }

    public RouletteEntry ToEntry()
    {
        return new RouletteEntry(Name.Trim(), RouletteEngine.ClampWeight(Weight), Enabled, ImagePath);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
