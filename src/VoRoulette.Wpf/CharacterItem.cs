using System.ComponentModel;
using System.Runtime.CompilerServices;
using VoRoulette.Core;

namespace VoRoulette.Wpf;

public sealed class CharacterItem : INotifyPropertyChanged
{
    private bool _enabled;
    private string _name;
    private double _weight;

    public CharacterItem(string name, double weight = 1, bool enabled = true)
    {
        _name = name;
        _weight = RouletteEngine.ClampWeight(weight);
        _enabled = enabled;
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

    public RouletteEntry ToEntry()
    {
        return new RouletteEntry(Name.Trim(), RouletteEngine.ClampWeight(Weight), Enabled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
