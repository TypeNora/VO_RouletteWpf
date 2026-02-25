using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using VoRoulette.Core;

namespace VoRoulette.Wpf;

public partial class MainWindow : Window
{
    private const string AppDataName = "VO_roulette_wpf";
    private const double ArcadeDefaultCellWidth = 243;
    private const double ArcadeDefaultCellHeight = 71;

    private readonly ObservableCollection<CharacterItem> _characters = [];
    private readonly List<RouletteEntry>[] _favorites = [[], [], []];
    private readonly Random _random = new();
    private readonly Dictionary<string, List<string>> _customPresets = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _arcadeTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _arcadeBlinkTimer = new() { Interval = TimeSpan.FromSeconds(0.25) };
    private readonly List<(Button Button, CharacterItem Item)> _arcadeButtons = [];
    private readonly Dictionary<Button, Border> _arcadeSelectionFrames = [];
    private readonly Dictionary<string, ImageSource?> _bannerCache = new(StringComparer.OrdinalIgnoreCase);
    private string _vootGeneMode = "DNA";
    private bool _restoringState;

    private bool _running;
    private bool _decelRequested;
    private DateTime _lastFrameAt;
    private DateTime _startAt;
    private double _omega;
    private double _omega0;
    private double _decelDurationSec;
    private double _decelElapsedSec;
    private bool _arcadeRunning;
    private bool _arcadeStopping;
    private int _arcadeCurrentIndex = -1;
    private int _arcadeTicksRemaining;
    private int _arcadeDecelTicks;
    private int _arcadeFinalIndex = -1;
    private string _arcadeFinalName = string.Empty;
    private bool _arcadeBlinkVisible = true;
    private DateTime _arcadeBlinkStartedAt;
    private double _arcadeCellWidth = ArcadeDefaultCellWidth;
    private double _arcadeCellHeight = ArcadeDefaultCellHeight;
    private string _selectedPreset = "オラタン";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly Encoding ShiftJisEncoding = CreateShiftJisEncoding();
    private static readonly string[] BannerFileExtensions = [".png", ".gif", ".bmp", ".jpg", ".jpeg"];

    public MainWindow()
    {
        InitializeComponent();

        CharacterGrid.ItemsSource = _characters;
        RefreshPresetComboBoxItems();
        WheelControl.PointerFlexEnabled = false;

        _arcadeTimer.Tick += ArcadeTimer_Tick;
        _arcadeBlinkTimer.Tick += ArcadeBlinkTimer_Tick;
        LoadState();
        UpdatePresetSelectionGuard();
        UpdateImagePathColumnVisibility();

        CompositionTarget.Rendering += OnRendering;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _arcadeTimer.Stop();
        _arcadeTimer.Tick -= ArcadeTimer_Tick;
        _arcadeBlinkTimer.Stop();
        _arcadeBlinkTimer.Tick -= ArcadeBlinkTimer_Tick;
        SaveState();
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var delta = (now - _lastFrameAt).TotalSeconds;
        if (delta <= 0)
        {
            return;
        }

        if (delta > 0.05)
        {
            delta = 0.05;
        }

        _lastFrameAt = now;

        var elapsed = (now - _startAt).TotalSeconds;
        if (!_decelRequested && elapsed >= ParseAndClamp(MaxTimeTextBox.Text, 1, 20) - ParseAndClamp(DecelTimeTextBox.Text, 0.2, 3))
        {
            RequestDecel();
        }

        if (_decelRequested)
        {
            _decelElapsedSec += delta;
            var remain = Math.Max(0, _decelDurationSec - _decelElapsedSec);
            _omega = remain > 0 ? _omega0 * (remain / _decelDurationSec) : 0;
            if (_omega <= 0.0001)
            {
                FinalizeStop();
                return;
            }
        }

        WheelControl.RotationRadians = NormalizeAngle(WheelControl.RotationRadians + _omega * delta);
        CurrentText.Text = WheelControl.PickCurrentName();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        RebuildWheel();
        var current = WheelControl.PickCurrentName();
        if (string.IsNullOrWhiteSpace(current))
        {
            CurrentText.Text = "有効キャラがありません";
            return;
        }

        _running = true;
        _decelRequested = false;
        _decelElapsedSec = 0;

        var maxTime = ParseAndClamp(MaxTimeTextBox.Text, 1, 20);
        var decel = ParseAndClamp(DecelTimeTextBox.Text, 0.2, 3);
        if (decel >= maxTime)
        {
            decel = Math.Max(0.2, maxTime * 0.4);
            DecelTimeTextBox.Text = decel.ToString("0.0");
        }

        _decelDurationSec = decel;
        _omega = 10 + _random.NextDouble() * 4;
        WheelControl.RotationRadians = _random.NextDouble() * Math.PI * 2;

        _startAt = DateTime.UtcNow;
        _lastFrameAt = _startAt;
        WheelControl.PointerFlexEnabled = true;

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        UpdatePresetSelectionGuard();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        RequestDecel();
    }

    private void RequestDecel()
    {
        if (!_running || _decelRequested)
        {
            return;
        }

        _decelRequested = true;
        _decelElapsedSec = 0;
        _decelDurationSec = ParseAndClamp(DecelTimeTextBox.Text, 0.2, 3);
        _omega0 = Math.Max(2, _omega);
        StopButton.IsEnabled = false;
    }

    private void FinalizeStop()
    {
        _running = false;
        _decelRequested = false;
        WheelControl.PointerFlexEnabled = false;
        WheelControl.RotationRadians = NormalizeAngle(WheelControl.RotationRadians + (_random.NextDouble() - 0.5) * (Math.PI / 90));

        var winner = WheelControl.PickCurrentName();
        CurrentText.Text = string.IsNullOrWhiteSpace(winner) ? "（未開始）" : winner;

        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        UpdatePresetSelectionGuard();
    }

    private void PresetComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_restoringState || _running || _arcadeRunning)
        {
            return;
        }

        if (PresetComboBox.SelectedItem is not string preset)
        {
            return;
        }

        ApplyPreset(preset);
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateImagePathColumnVisibility();
    }

    private void UpdateImagePathColumnVisibility()
    {
        if (ImagePathColumn is null || MainTabControl is null || RouletteTab is null)
        {
            return;
        }

        var rouletteSelected = ReferenceEquals(MainTabControl.SelectedItem, RouletteTab);
        ImagePathColumn.Visibility = rouletteSelected
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RefreshPresetComboBoxItems()
    {
        if (PresetComboBox is null)
        {
            return;
        }

        PresetComboBox.Items.Clear();
        foreach (var key in Presets.All.Keys)
        {
            PresetComboBox.Items.Add(key);
        }
    }

    private void ApplyPreset(string preset)
    {
        if (!Presets.All.TryGetValue(preset, out var names))
        {
            return;
        }

        _characters.Clear();
        foreach (var name in names)
        {
            var item = CreateCharacter(name, 1, true);
            _characters.Add(item);
        }

        _selectedPreset = preset;
        UpdateArcadeModeControls();
        RebuildWheel();
        SaveState();
    }

    private void RegisterPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _arcadeRunning)
        {
            return;
        }

        var presetName = (NewPresetNameTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(presetName))
        {
            PresetStatusText.Text = "プリセット名を入力してください。";
            return;
        }

        if (Presets.IsBuiltIn(presetName))
        {
            PresetStatusText.Text = "標準プリセット名は上書きできません。";
            return;
        }

        var names = _characters
            .Select(x => (x.Name ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
        if (names.Count == 0)
        {
            PresetStatusText.Text = "登録対象のキャラクターがありません。";
            return;
        }

        if (!Presets.Register(presetName, names, overwrite: true))
        {
            PresetStatusText.Text = "プリセット登録に失敗しました。";
            return;
        }

        _customPresets[presetName] = names;
        _selectedPreset = presetName;
        _restoringState = true;
        try
        {
            RefreshPresetComboBoxItems();
            PresetComboBox.SelectedItem = presetName;
        }
        finally
        {
            _restoringState = false;
        }
        PresetStatusText.Text = $"プリセット「{presetName}」を登録しました。";
        SaveState();
    }

    private void SaveJson_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _arcadeRunning)
        {
            return;
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dialog = new SaveFileDialog
            {
                Filter = "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                DefaultExt = ".json",
                AddExtension = true,
                FileName = "state.json",
                InitialDirectory = baseDir,
                Title = "JSON保存先を選択"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var json = JsonSerializer.Serialize(BuildCurrentState(), JsonOptions);
            File.WriteAllText(dialog.FileName, json, ShiftJisEncoding);
            PresetStatusText.Text = $"JSON保存しました: {dialog.FileName}";
        }
        catch
        {
            PresetStatusText.Text = "JSON保存に失敗しました。";
        }
    }

    private void LoadJson_Click(object sender, RoutedEventArgs e)
    {
        if (_running || _arcadeRunning)
        {
            return;
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dialog = new OpenFileDialog
            {
                Filter = "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                Multiselect = false,
                FileName = "state.json",
                InitialDirectory = baseDir,
                Title = "JSON読込元を選択"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var json = File.ReadAllText(dialog.FileName, ShiftJisEncoding);
            var state = JsonSerializer.Deserialize<RouletteState>(json, JsonOptions);
            if (state is null)
            {
                PresetStatusText.Text = "JSON読込に失敗しました。";
                return;
            }

            ApplyState(state);
            SaveState();
            PresetStatusText.Text = $"JSON読込しました: {dialog.FileName}";
        }
        catch
        {
            PresetStatusText.Text = "JSON読込に失敗しました。";
        }
    }

    private void AllOn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _characters)
        {
            item.Enabled = true;
        }

        RebuildWheel();
        SaveState();
    }

    private void AllOff_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _characters)
        {
            item.Enabled = false;
        }

        RebuildWheel();
        SaveState();
    }

    private void Invert_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _characters)
        {
            item.Enabled = !item.Enabled;
        }

        RebuildWheel();
        SaveState();
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        ClearCharacterList();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = (NewNameTextBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var weight = ParseAndClamp(NewWeightTextBox.Text, 0.1, 10);
        var item = CreateCharacter(name, weight, true);
        _characters.Add(item);

        NewNameTextBox.Text = string.Empty;
        NewWeightTextBox.Text = "1";

        RebuildWheel();
        SaveState();
    }

    private void SelectImagePath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CharacterItem item })
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var initialDirectory = baseDir;
        var existing = (item.ImagePath ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            var resolved = ResolveCustomImagePath(existing);
            var dir = Path.GetDirectoryName(resolved);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                initialDirectory = dir;
            }
        }

        var dialog = new OpenFileDialog
        {
            Filter = "画像ファイル (*.png;*.gif;*.bmp;*.jpg;*.jpeg)|*.png;*.gif;*.bmp;*.jpg;*.jpeg|すべてのファイル (*.*)|*.*",
            Multiselect = false,
            InitialDirectory = initialDirectory,
            Title = "画像ファイルを選択"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selected = dialog.FileName;
        var normalized = NormalizeStoredImagePath(selected, baseDir);
        item.ImagePath = normalized;
        RebuildWheel();
        SaveState();
    }

    private void RemoveCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: CharacterItem item })
        {
            return;
        }

        item.PropertyChanged -= CharacterItem_PropertyChanged;
        _characters.Remove(item);

        RebuildWheel();
        SaveState();
    }

    private void FavoriteMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshFavoriteButtons();
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag } || !int.TryParse(tag, out var slot) || slot < 0 || slot > 2)
        {
            return;
        }

        if (FavoriteSaveRadio.IsChecked == true)
        {
            _favorites[slot] = GetEntries().ToList();
            FavoriteStatusText.Text = $"お気に入り{slot + 1}に保存しました。";
            SaveState();
            RefreshFavoriteButtons();
            return;
        }

        if (_favorites[slot].Count == 0)
        {
            FavoriteStatusText.Text = $"お気に入り{slot + 1}は未保存です。";
            return;
        }

        _characters.Clear();
        foreach (var entry in _favorites[slot])
        {
            _characters.Add(CreateCharacter(entry.Name, entry.Weight, entry.Enabled, entry.ImagePath));
        }

        FavoriteStatusText.Text = $"お気に入り{slot + 1}を読み込みました。";
        RebuildWheel();
        SaveState();
        RefreshFavoriteButtons();
    }

    private void CharacterItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RebuildWheel();
        SaveState();
    }

    private void RefreshFavoriteButtons()
    {
        if (FavoriteLoadRadio is null || FavoriteButton1 is null || FavoriteButton2 is null || FavoriteButton3 is null)
        {
            return;
        }

        var loadingMode = FavoriteLoadRadio.IsChecked == true;
        var buttons = new[] { FavoriteButton1, FavoriteButton2, FavoriteButton3 };
        for (var i = 0; i < buttons.Length; i += 1)
        {
            var saved = _favorites[i].Count > 0;
            buttons[i].IsEnabled = !loadingMode || saved;
            buttons[i].FontWeight = saved ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private CharacterItem CreateCharacter(string name, double weight, bool enabled, string? imagePath = null)
    {
        var item = new CharacterItem(name, weight, enabled, imagePath);
        item.PropertyChanged += CharacterItem_PropertyChanged;
        return item;
    }

    private void RebuildWheel()
    {
        WheelControl.RebuildSegments(GetEntries());
        RebuildArcadePanels();
        if (!_running)
        {
            CurrentText.Text = WheelControl.PickCurrentName();
        }
    }

    private IEnumerable<RouletteEntry> GetEntries()
    {
        return _characters.Select(c => c.ToEntry());
    }

    private void ArcadeStart_Click(object sender, RoutedEventArgs e)
    {
        if (_arcadeRunning)
        {
            return;
        }

        RebuildArcadePanels();
        var selectable = GetSelectableArcadeIndices();
        if (selectable.Count == 0)
        {
            ArcadeCurrentText.Text = "有効(ON)キャラがありません";
            return;
        }

        _arcadeRunning = true;
        _arcadeStopping = false;
        _arcadeBlinkTimer.Stop();
        _arcadeFinalIndex = -1;
        _arcadeBlinkVisible = true;
        _arcadeCurrentIndex = NextArcadeWeightedIndex(selectable);
        if (_arcadeCurrentIndex < 0)
        {
            _arcadeRunning = false;
            _arcadeStopping = false;
            ArcadeCurrentText.Text = "有効(ON)キャラがありません";
            UpdatePresetSelectionGuard();
            return;
        }
        var totalSec = ParseAndClamp(MaxTimeTextBox.Text, 1, 20);
        var decelSec = ParseAndClamp(DecelTimeTextBox.Text, 0.2, 3);
        if (decelSec >= totalSec)
        {
            decelSec = Math.Max(0.2, totalSec * 0.4);
            DecelTimeTextBox.Text = decelSec.ToString("0.0");
        }
        _arcadeTicksRemaining = Math.Max(1, (int)Math.Round(totalSec / _arcadeTimer.Interval.TotalSeconds));
        _arcadeDecelTicks = Math.Max(1, (int)Math.Round(decelSec / _arcadeTimer.Interval.TotalSeconds));

        ArcadeStartButton.IsEnabled = false;
        ArcadeStopButton.IsEnabled = true;
        _arcadeTimer.Interval = TimeSpan.FromMilliseconds(80);
        ApplyArcadeHighlight(_arcadeCurrentIndex);
        _arcadeTimer.Start();
        UpdatePresetSelectionGuard();
    }

    private void ArcadeStop_Click(object sender, RoutedEventArgs e)
    {
        if (!_arcadeRunning)
        {
            return;
        }

        _arcadeStopping = true;
        _arcadeTicksRemaining = Math.Min(_arcadeTicksRemaining, _arcadeDecelTicks);
        ArcadeStopButton.IsEnabled = false;
    }

    private void ArcadeTimer_Tick(object? sender, EventArgs e)
    {
        if (!_arcadeRunning)
        {
            return;
        }

        var selectable = GetSelectableArcadeIndices();
        if (selectable.Count == 0)
        {
            FinalizeArcadeSelection("有効(ON)キャラがありません");
            return;
        }

        _arcadeCurrentIndex = NextArcadeWeightedIndex(selectable);
        if (_arcadeCurrentIndex < 0 || _arcadeCurrentIndex >= _arcadeButtons.Count)
        {
            FinalizeArcadeSelection("有効(ON)キャラがありません");
            return;
        }
        ApplyArcadeHighlight(_arcadeCurrentIndex);
        ArcadeCurrentText.Text = _arcadeButtons[_arcadeCurrentIndex].Item.Name;

        if (_arcadeTicksRemaining > 0)
        {
            _arcadeTicksRemaining -= _arcadeStopping ? 2 : 1;
        }

        if (!_arcadeStopping && _arcadeTicksRemaining <= _arcadeDecelTicks)
        {
            _arcadeStopping = true;
            ArcadeStopButton.IsEnabled = false;
        }

        if (_arcadeTicksRemaining <= 0)
        {
            FinalizeArcadeSelection(_arcadeButtons[_arcadeCurrentIndex].Item.Name);
            return;
        }

        if (_arcadeStopping)
        {
            var progressed = 1.0 - (Math.Max(_arcadeTicksRemaining, 0) / (double)Math.Max(1, _arcadeDecelTicks));
            progressed = Math.Clamp(progressed, 0, 1);
            var intervalMs = 80 + (220 * progressed); // 80ms -> 300msへ徐々に減速
            _arcadeTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }
        else
        {
            _arcadeTimer.Interval = TimeSpan.FromMilliseconds(80);
        }
    }

    private void FinalizeArcadeSelection(string resultText)
    {
        _arcadeTimer.Stop();
        _arcadeTimer.Interval = TimeSpan.FromMilliseconds(80);
        _arcadeRunning = false;
        _arcadeStopping = false;
        ArcadeStartButton.IsEnabled = true;
        ArcadeStopButton.IsEnabled = false;
        ArcadeCurrentText.Text = resultText;
        UpdatePresetSelectionGuard();

        _arcadeFinalIndex = _arcadeCurrentIndex;
        _arcadeFinalName = _arcadeFinalIndex >= 0 && _arcadeFinalIndex < _arcadeButtons.Count
            ? _arcadeButtons[_arcadeFinalIndex].Item.Name
            : string.Empty;
        _arcadeBlinkVisible = true;
        _arcadeBlinkStartedAt = DateTime.UtcNow;
        if (_arcadeFinalIndex >= 0)
        {
            _arcadeBlinkTimer.Start();
        }
    }

    private void UpdatePresetSelectionGuard()
    {
        if (PresetComboBox is null)
        {
            return;
        }

        var locked = _running || _arcadeRunning;
        PresetComboBox.IsEnabled = !locked;
        if (NewPresetNameTextBox is not null)
        {
            NewPresetNameTextBox.IsEnabled = !locked;
        }
        if (LoadJsonButton is not null)
        {
            LoadJsonButton.IsEnabled = !locked;
        }
        if (SaveJsonButton is not null)
        {
            SaveJsonButton.IsEnabled = !locked;
        }
    }

    private void ArcadeBlinkTimer_Tick(object? sender, EventArgs e)
    {
        if (_arcadeFinalIndex < 0 || _arcadeFinalIndex >= _arcadeButtons.Count)
        {
            _arcadeBlinkTimer.Stop();
            return;
        }

        var elapsed = DateTime.UtcNow - _arcadeBlinkStartedAt;
        if (elapsed.TotalSeconds >= 2.0)
        {
            _arcadeBlinkTimer.Stop();
            _arcadeBlinkVisible = true;
            for (var i = 0; i < _arcadeButtons.Count; i += 1)
            {
                if (_arcadeSelectionFrames.TryGetValue(_arcadeButtons[i].Button, out var frame))
                {
                    frame.BorderBrush = GetArcadeHighlightBrush();
                    frame.BorderThickness = i == _arcadeFinalIndex ? new Thickness(10) : new Thickness(0);
                }
            }
            return;
        }

        _arcadeBlinkVisible = !_arcadeBlinkVisible;
        for (var i = 0; i < _arcadeButtons.Count; i += 1)
        {
            if (_arcadeSelectionFrames.TryGetValue(_arcadeButtons[i].Button, out var frame))
            {
                frame.BorderBrush = GetArcadeHighlightBrush();
                frame.BorderThickness = i == _arcadeFinalIndex && _arcadeBlinkVisible
                    ? new Thickness(10)
                    : new Thickness(0);
            }
        }
    }

    private int NextArcadeWeightedIndex(IReadOnlyList<int> selectable)
    {
        if (selectable.Count == 0)
        {
            return -1;
        }

        double total = 0;
        foreach (var index in selectable)
        {
            if (index < 0 || index >= _arcadeButtons.Count)
            {
                continue;
            }
            total += RouletteEngine.ClampWeight(_arcadeButtons[index].Item.Weight);
        }

        if (total <= 0)
        {
            return selectable[_random.Next(selectable.Count)];
        }

        var roll = _random.NextDouble() * total;
        double cumulative = 0;
        foreach (var index in selectable)
        {
            if (index < 0 || index >= _arcadeButtons.Count)
            {
                continue;
            }
            cumulative += RouletteEngine.ClampWeight(_arcadeButtons[index].Item.Weight);
            if (roll <= cumulative)
            {
                return index;
            }
        }

        return selectable[^1];
    }

    private List<int> GetSelectableArcadeIndices()
    {
        var result = new List<int>();
        for (var i = 0; i < _arcadeButtons.Count; i += 1)
        {
            if (_arcadeButtons[i].Item.Enabled)
            {
                result.Add(i);
            }
        }

        return result;
    }

    private void RebuildArcadePanels()
    {
        if (ArcadeGridPanel is null || ArcadeCurrentText is null)
        {
            return;
        }

        ArcadeGridPanel.Children.Clear();
        _arcadeButtons.Clear();
        _arcadeSelectionFrames.Clear();
        _arcadeBlinkTimer.Stop();
        var previousFinalName = _arcadeFinalName;
        _arcadeFinalIndex = -1;
        _arcadeBlinkVisible = true;
        _arcadeCurrentIndex = -1;

        if (IsVootPreset())
        {
            BuildVootArcadeGrid(ArcadeGridPanel);
        }
        else
        {
            for (var i = 0; i < _characters.Count; i += 1)
            {
                AddArcadeItemIfEnabled(ArcadeGridPanel, _characters[i]);
            }
        }

        if (_arcadeButtons.Count == 0)
        {
            ArcadeCurrentText.Text = "（未開始）";
        }
        else if (!_arcadeRunning)
        {
            ArcadeCurrentText.Text = _arcadeButtons[0].Item.Name;
        }

        UpdateArcadeModeControls();
        ApplyArcadeHighlight(-1);

        if (!_arcadeRunning && !string.IsNullOrWhiteSpace(previousFinalName))
        {
            var restored = _arcadeButtons.FindIndex(x => string.Equals(x.Item.Name, previousFinalName, StringComparison.Ordinal));
            if (restored >= 0)
            {
                _arcadeFinalIndex = restored;
                _arcadeCurrentIndex = restored;
                _arcadeFinalName = previousFinalName;
                ArcadeCurrentText.Text = previousFinalName;
                ApplyArcadeHighlight(restored);
            }
        }
    }

    private void BuildVootArcadeGrid(Panel panel)
    {
        var map = BuildAliasItemMap();
        foreach (var slot in VootGridSlots)
        {
            if (slot == "EMPTY") continue;

            var slotKey = NormalizeArcadeKey(slot);
            if (map.TryGetValue(slotKey, out var item))
            {
                AddArcadeItemIfEnabled(panel, item);
                continue;
            }
        }
    }

    private void AddArcadeItemIfEnabled(Panel panel, CharacterItem item)
    {
        if (!item.Enabled)
        {
            return;
        }

        var button = CreateArcadeBannerButton(item);
        _arcadeButtons.Add((button, item));
        panel.Children.Add(button);
    }

    private Dictionary<string, CharacterItem> BuildAliasItemMap()
    {
        var map = new Dictionary<string, CharacterItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _characters)
        {
            var alias = NormalizeArcadeKey(ResolveArcadeAlias(item.Name));
            if (!map.ContainsKey(alias))
            {
                map[alias] = item;
            }
        }

        return map;
    }

    private FrameworkElement CreateEmptyArcadeCell()
    {
        return new Border
        {
            Width = _arcadeCellWidth,
            Height = _arcadeCellHeight,
            Background = Brushes.Transparent
        };
    }

    private FrameworkElement CreateMissingArcadeCell(string slot)
    {
        return new Border
        {
            Width = _arcadeCellWidth,
            Height = _arcadeCellHeight,
            Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = slot,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private Button CreateArcadeBannerButton(CharacterItem item)
    {
        var source = ResolveBannerImage(item);
        var (cellWidth, cellHeight) = GetCellSizeFromImage(source);
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Source = source,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var root = new Grid();
        root.Background = source is null
            ? new SolidColorBrush(Color.FromRgb(12, 12, 12))
            : Brushes.Transparent;
        root.ClipToBounds = true;
        root.Children.Add(image);
        if (source is null)
        {
            var text = new TextBlock
            {
                Text = item.Name,
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            root.Children.Add(text);
        }

        var selectionFrame = new Border
        {
            BorderBrush = GetArcadeHighlightBrush(),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0),
            IsHitTestVisible = false
        };
        root.Children.Add(selectionFrame);

        var button = new Button
        {
            Content = root,
            Width = cellWidth,
            Height = cellHeight,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Center,
            Tag = item,
            Opacity = item.Enabled ? 1 : 0.45,
            IsEnabled = item.Enabled
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ArcadeImageButtonStyle");
        _arcadeSelectionFrames[button] = selectionFrame;
        button.Click += ArcadeBanner_Click;
        return button;
    }

    private (double Width, double Height) GetCellSizeFromImage(ImageSource? source)
    {
        if (source is BitmapSource bitmap)
        {
            var dpiX = bitmap.DpiX <= 0 ? 96 : bitmap.DpiX;
            var dpiY = bitmap.DpiY <= 0 ? 96 : bitmap.DpiY;
            var width = bitmap.PixelWidth * 96.0 / dpiX;
            var height = bitmap.PixelHeight * 96.0 / dpiY;
            if (width > 0 && height > 0)
            {
                _arcadeCellWidth = Math.Min(width, ArcadeDefaultCellWidth);
                _arcadeCellHeight = Math.Min(height, ArcadeDefaultCellHeight);
                return (_arcadeCellWidth, _arcadeCellHeight);
            }
        }

        _arcadeCellWidth = ArcadeDefaultCellWidth;
        _arcadeCellHeight = ArcadeDefaultCellHeight;
        return (_arcadeCellWidth, _arcadeCellHeight);
    }

    private void ArcadeBanner_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CharacterItem item })
        {
            return;
        }

        var index = _arcadeButtons.FindIndex(x => ReferenceEquals(x.Item, item));
        if (index < 0)
        {
            return;
        }

        _arcadeCurrentIndex = index;
        ApplyArcadeHighlight(index);
        ArcadeCurrentText.Text = item.Name;
    }

    private void ApplyArcadeHighlight(int selectedIndex)
    {
        for (var i = 0; i < _arcadeButtons.Count; i += 1)
        {
            var selected = i == selectedIndex;
            if (_arcadeSelectionFrames.TryGetValue(_arcadeButtons[i].Button, out var frame))
            {
                frame.BorderBrush = GetArcadeHighlightBrush();
                frame.BorderThickness = selected ? new Thickness(10) : new Thickness(0);
            }
        }
    }

    private ImageSource? ResolveBannerImage(CharacterItem item)
    {
        var customPath = (item.ImagePath ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            var resolvedPath = ResolveCustomImagePath(customPath);
            var customKey = $"CUSTOM|{resolvedPath}";
            if (_bannerCache.TryGetValue(customKey, out var customCached))
            {
                if (customCached is not null)
                {
                    return customCached;
                }
            }
            else
            {
                var customImage = LoadImageIfExists(resolvedPath);
                _bannerCache[customKey] = customImage;
                if (customImage is not null)
                {
                    return customImage;
                }
            }
        }

        var presetDir = ResolvePresetDirectoryName();
        var mode = IsVootPreset() ? _vootGeneMode : "NONE";
        var key = $"{presetDir}|{mode}|{NormalizeAssetKey(item.Name)}";
        if (_bannerCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var baseDir = Path.Combine(AppContext.BaseDirectory, "assets", "arcade-banners", "source", presetDir);
        foreach (var path in BuildBannerCandidatePaths(baseDir, item.Name))
        {
            var bitmap = LoadImageIfExists(path);
            if (bitmap is null)
            {
                continue;
            }

            _bannerCache[key] = bitmap;
            return bitmap;
        }

        _bannerCache[key] = null;
        return null;
    }

    private static string ResolveCustomImagePath(string imagePath)
    {
        if (Path.IsPathRooted(imagePath))
        {
            return imagePath;
        }

        return Path.Combine(AppContext.BaseDirectory, imagePath);
    }

    private static string NormalizeStoredImagePath(string fullPath, string baseDir)
    {
        try
        {
            var relative = Path.GetRelativePath(baseDir, fullPath);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            {
                return relative;
            }
        }
        catch
        {
        }

        return fullPath;
    }

    private static ImageSource? LoadImageIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeAssetKey(string name)
    {
        var raw = (name ?? string.Empty).Trim().ToLowerInvariant();
        var chars = raw.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length > 0 ? new string(chars) : "unknown";
    }

    private static string ResolveArcadeAlias(string name)
    {
        return name switch
        {
            "ライデン" => "RAIDEN",
            "シュタイン" => "STEINVOK",
            "グリス" => "GRYSVOK",
            "テムジン" => "TEMJIN",
            "テンパチ" => "1080SP",
            "バル" => "BALBADOS",
            "エンジェ" => "ANGELAN",
            "スぺ" => "SPECINEFF",
            "スペシネフ" => "SPECINEFF",
            "アファームドS" => "APHARMD_S",
            "アファームドB" => "APHARMD_B",
            "アファームドC" => "APHARMD_C",
            "ストライカー" => "APHARMD_S",
            "バトラー" => "APHARMD_B",
            "コマンダー" => "APHARMD_C",
            "サイファー" => "CYPHER",
            "フェイ" => "FEIYEN",
            "フェイ・イェン" => "FEIYEN",
            "ドルドレイ" => "DORDRAY",
            "ドル" => "DORDRAY",
            "エンジェラン" => "ANGELAN",
            "アジム" => "AJIM",
            _ => name.ToUpperInvariant()
        };
    }

    private static string NormalizeArcadeKey(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private IEnumerable<string> BuildBannerCandidatePaths(string baseDir, string name)
    {
        var candidates = BuildNameCandidates(name).ToArray();
        if (IsVootPreset())
        {
            foreach (var stem in candidates)
            {
                foreach (var ext in BannerFileExtensions)
                {
                    yield return Path.Combine(baseDir, $"{_vootGeneMode}_{stem}{ext}");
                    yield return Path.Combine(baseDir, $"_{_vootGeneMode}_{stem}{ext}");
                    yield return Path.Combine(baseDir, $"_{_vootGeneMode}{stem}{ext}");
                }
            }
        }

        foreach (var stem in candidates)
        {
            foreach (var ext in BannerFileExtensions)
            {
                yield return Path.Combine(baseDir, $"{stem}{ext}");
            }
        }
    }

    private static IEnumerable<string> BuildNameCandidates(string name)
    {
        var raw = (name ?? string.Empty).Trim();
        var alias = ResolveArcadeAlias(raw);
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCandidate(candidates, raw);
        AddCandidate(candidates, alias);
        AddCandidate(candidates, NormalizeAssetKey(raw));
        AddCandidate(candidates, NormalizeAssetKey(alias));
        AddCandidate(candidates, raw.Replace(" ", string.Empty));
        AddCandidate(candidates, alias.Replace(" ", string.Empty));
        AddCandidate(candidates, alias.Replace("-", string.Empty));
        return candidates;
    }

    private static void AddCandidate(ISet<string> set, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            set.Add(value);
        }
    }

    private string ResolvePresetDirectoryName()
    {
        return _selectedPreset switch
        {
            "OMG" => "OMG",
            "オラタン" => "VOOT",
            "フォース" => "FORCE",
            "禁書VO" => "VOINDEX",
            _ => NormalizeAssetKey(_selectedPreset).ToUpperInvariant()
        };
    }

    private bool IsVootPreset()
    {
        return ResolvePresetDirectoryName().Equals("VOOT", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateArcadeModeControls()
    {
        if (VootToggleButton is null)
        {
            return;
        }

        var visible = IsVootPreset();
        VootToggleButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        VootToggleButton.Content = _vootGeneMode;
    }

    private void VootToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _vootGeneMode = string.Equals(_vootGeneMode, "DNA", StringComparison.OrdinalIgnoreCase) ? "RNA" : "DNA";
        _bannerCache.Clear();
        RebuildArcadePanels();
    }

    private Brush GetArcadeHighlightBrush()
    {
        return string.Equals(_vootGeneMode, "DNA", StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Color.FromRgb(255, 145, 40))
            : new SolidColorBrush(Color.FromRgb(124, 252, 0));
    }

    private static readonly string[] VootGridSlots =
    [
        "RAIDEN", "SPECINEFF",
        "STEINVOK", "APHARMD_S",
        "GRYSVOK", "APHARMD_B",
        "TEMJIN", "APHARMD_C",
        "1080SP", "CYPHER",
        "BALBADOS", "FEIYEN",
        "ANGELAN", "DORDRAY",
        "AJIM", "EMPTY"
    ];

    private static double ParseAndClamp(string? text, double min, double max)
    {
        var parsed = double.TryParse(text, out var value) ? value : min;
        if (double.IsNaN(parsed))
        {
            parsed = min;
        }

        if (parsed < min)
        {
            parsed = min;
        }

        if (parsed > max)
        {
            parsed = max;
        }

        return parsed;
    }

    private static double NormalizeAngle(double angle)
    {
        var tau = Math.PI * 2;
        var normalized = angle % tau;
        return normalized < 0 ? normalized + tau : normalized;
    }

    private void LoadState()
    {
        try
        {
            ApplyState(RouletteStateStore.Load(AppDataName));
        }
        catch
        {
            ApplyState(new RouletteState());
        }
    }

    private void ApplyState(RouletteState state)
    {
        _restoringState = true;
        try
        {
            Presets.ResetToBuiltIn();
            _customPresets.Clear();
            foreach (var (name, list) in state.CustomPresets ?? new Dictionary<string, List<string>>())
            {
                if (Presets.IsBuiltIn(name))
                {
                    continue;
                }

                var cleaned = (list ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (cleaned.Count == 0)
                {
                    continue;
                }

                if (Presets.Register(name, cleaned))
                {
                    _customPresets[name] = cleaned;
                }
            }

            RefreshPresetComboBoxItems();
            _selectedPreset = string.IsNullOrWhiteSpace(state.SelectedPreset) ? "オラタン" : state.SelectedPreset;
            if (!Presets.All.ContainsKey(_selectedPreset))
            {
                _selectedPreset = "オラタン";
            }

            PresetComboBox.SelectedItem = _selectedPreset;

            var loaded = RouletteEngine.Normalize(state.Entries).ToList();
            if (loaded.Count == 0)
            {
                loaded = Presets.All["オラタン"].Select(name => new RouletteEntry(name, 1, true)).ToList();
            }

            _characters.Clear();
            foreach (var entry in loaded)
            {
                _characters.Add(CreateCharacter(entry.Name, entry.Weight, entry.Enabled, entry.ImagePath));
            }

            for (var i = 0; i < 3; i += 1)
            {
                _favorites[i].Clear();
                var favorites = state.Favorites ?? new List<List<RouletteEntry>?> { null, null, null };
                var src = i < favorites.Count ? favorites[i] : null;
                if (src is null)
                {
                    continue;
                }

                _favorites[i].AddRange(RouletteEngine.Normalize(src));
            }

            FavoriteStatusText.Text = string.Empty;
            PresetStatusText.Text = string.Empty;
            RefreshFavoriteButtons();
            RebuildWheel();
        }
        finally
        {
            _restoringState = false;
        }
    }

    private RouletteState BuildCurrentState()
    {
        return new RouletteState
        {
            Entries = GetEntries().ToList(),
            SelectedPreset = _selectedPreset,
            Favorites = _favorites.Select(f => f.Count == 0 ? null : f.ToList()).ToList(),
            CustomPresets = _customPresets.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.Ordinal)
        };
    }

    private void ClearCharacterList()
    {
        foreach (var item in _characters)
        {
            item.PropertyChanged -= CharacterItem_PropertyChanged;
        }

        _characters.Clear();
        RebuildWheel();
        FavoriteStatusText.Text = "リストを全削除しました。";
        SaveState();
    }

    private static Encoding CreateShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private void SaveState()
    {
        RouletteStateStore.Save(AppDataName, BuildCurrentState());
    }
}
