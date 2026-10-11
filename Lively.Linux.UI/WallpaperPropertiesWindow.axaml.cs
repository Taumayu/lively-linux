using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lively.Common.Helpers;
using Lively.Grpc.Common.Proto.Desktop;
using Lively.Models;
using Lively.Models.LivelyControls;
using Lively.Models.Message;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lively.Linux.UI;

public partial class WallpaperPropertiesWindow : Window
{
    private readonly LivelyInfoModel _info;
    private readonly DesktopService.DesktopServiceClient _client;
    private readonly string _displayId;
    private readonly string _propertyFilePath;
    private readonly string _wallpaperDir;
    private readonly string _backupFilePath;

    private Dictionary<string, ControlModel> _controls = new();
    private readonly Dictionary<string, object> _defaultValues = new();
    private readonly Dictionary<string, DispatcherTimer> _debounceTimers = new();

    public WallpaperPropertiesWindow()
    {
        InitializeComponent();
        _info = new LivelyInfoModel();
        _client = null!;
        _displayId = "eDP-1";
        _propertyFilePath = "";
        _wallpaperDir = "";
        _backupFilePath = "";
    }

    public WallpaperPropertiesWindow(LivelyInfoModel info, DesktopService.DesktopServiceClient client, string displayId)
    {
        InitializeComponent();
        _info = info;
        _client = client;
        _displayId = displayId;

        _wallpaperDir = Path.GetDirectoryName(info.Id) ?? "";
        _propertyFilePath = Path.Combine(_wallpaperDir, "LivelyProperties.json");
        _backupFilePath = Path.Combine(_wallpaperDir, "LivelyProperties.json.bak");

        Title = $"Paramètres - {_info.Title}";
        TitleTextBlock.Text = _info.Title;
        AuthorTextBlock.Text = $"Par {_info.Author} • {_info.Type}";
        DescTextBlock.Text = _info.Desc ?? "";

        if (!string.IsNullOrEmpty(_info.Thumbnail))
        {
            AsyncImageLoader.ImageLoader.SetSource(ThumbnailImage, _info.Thumbnail);
        }

        BackupInitialProperties();
        BuildControls();
    }

    private void BackupInitialProperties()
    {
        try
        {
            if (File.Exists(_propertyFilePath) && !File.Exists(_backupFilePath))
            {
                File.Copy(_propertyFilePath, _backupFilePath, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Backup error: {ex.Message}");
        }
    }

    private void BuildControls()
    {
        ControlsContainer.Children.Clear();

        if (!File.Exists(_propertyFilePath))
        {
            ControlsContainer.Children.Add(new TextBlock
            {
                Text = "Ce fond d'écran ne possède aucun fichier de configuration LivelyProperties.json.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(10)
            });
            return;
        }

        try
        {
            _controls = LivelyPropertyUtil.GetControls(_propertyFilePath);
        }
        catch (Exception ex)
        {
            ControlsContainer.Children.Add(new TextBlock
            {
                Text = $"Erreur lors de la lecture des paramètres: {ex.Message}",
                Foreground = Brushes.Tomato,
                Margin = new Thickness(10)
            });
            return;
        }

        if (_controls.Count == 0)
        {
            ControlsContainer.Children.Add(new TextBlock
            {
                Text = "Aucun paramètre personnalisable trouvé.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(10)
            });
            return;
        }

        foreach (var kvp in _controls)
        {
            var key = kvp.Key;
            var control = kvp.Value;
            if (string.IsNullOrEmpty(control.Name))
                control.Name = key;

            var card = CreateControlCard(control);
            if (card != null)
            {
                ControlsContainer.Children.Add(card);
            }
        }
    }

    private Control? CreateControlCard(ControlModel control)
    {
        var border = new Border
        {
            Background = Brush.Parse("#24242A"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var mainPanel = new StackPanel { Spacing = 8 };
        border.Child = mainPanel;

        switch (control)
        {
            case SliderModel slider:
                BuildSliderControl(slider, mainPanel);
                break;
            case FolderDropdownModel folderDropdown:
                BuildFolderDropdownControl(folderDropdown, mainPanel);
                break;
            case DropdownModel dropdown:
                BuildDropdownControl(dropdown, mainPanel);
                break;
            case CheckboxModel checkbox:
                BuildCheckboxControl(checkbox, mainPanel);
                break;
            case ColorPickerModel colorPicker:
                BuildColorPickerControl(colorPicker, mainPanel);
                break;
            case ButtonModel button:
                BuildButtonControl(button, mainPanel);
                break;
            default:
                return null;
        }

        return border;
    }

    private void BuildSliderControl(SliderModel slider, StackPanel panel)
    {
        if (!_defaultValues.ContainsKey(slider.Name))
            _defaultValues[slider.Name] = slider.Value;

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, Auto")
        };

        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(slider.Text) ? slider.Name : slider.Text,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        headerGrid.Children.Add(label);

        string FormatValue(double val) => slider.Step < 1 ? val.ToString("0.00") : Math.Round(val).ToString("0");

        var badgeBorder = new Border
        {
            Background = Brush.Parse("#32323D"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var valueBadge = new TextBlock
        {
            Text = FormatValue(slider.Value),
            Foreground = Brush.Parse("#00A2FF"),
            FontWeight = FontWeight.Bold,
            FontSize = 13
        };
        badgeBorder.Child = valueBadge;
        Grid.SetColumn(badgeBorder, 1);
        headerGrid.Children.Add(badgeBorder);

        panel.Children.Add(headerGrid);

        if (!string.IsNullOrEmpty(slider.Help))
        {
            panel.Children.Add(new TextBlock
            {
                Text = slider.Help,
                Foreground = Brush.Parse("#8E8E98"),
                FontSize = 11
            });
        }

        var avaloniaSlider = new Slider
        {
            Minimum = slider.Min,
            Maximum = slider.Max,
            Value = slider.Value,
            TickFrequency = slider.Step > 0 ? slider.Step : 1,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 4, 0, 0)
        };

        avaloniaSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                var val = avaloniaSlider.Value;
                valueBadge.Text = FormatValue(val);
                slider.Value = val;

                Debounce(slider.Name, 40, () =>
                {
                    SendIpcMessage(new LivelySlider
                    {
                        Name = slider.Name,
                        Value = val,
                        Step = slider.Step
                    });
                    SaveProperty(slider.Name, val);
                });
            }
        };

        panel.Children.Add(avaloniaSlider);
    }

    private void BuildFolderDropdownControl(FolderDropdownModel fd, StackPanel panel)
    {
        if (!_defaultValues.ContainsKey(fd.Name))
            _defaultValues[fd.Name] = fd.Value;

        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(fd.Text) ? fd.Name : fd.Text,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14
        };
        panel.Children.Add(label);

        if (!string.IsNullOrEmpty(fd.Help))
        {
            panel.Children.Add(new TextBlock
            {
                Text = fd.Help,
                Foreground = Brush.Parse("#8E8E98"),
                FontSize = 11
            });
        }

        var folderPath = Path.Combine(_wallpaperDir, fd.Folder ?? "");
        if (!Directory.Exists(folderPath))
        {
            try { Directory.CreateDirectory(folderPath); } catch { }
        }

        var comboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brush.Parse("#1A1A1E"),
            Foreground = Brushes.White
        };

        void RefreshFiles(string? selectItem = null)
        {
            var files = new List<string>();
            if (Directory.Exists(folderPath))
            {
                var dirFiles = Directory.GetFiles(folderPath);
                foreach (var f in dirFiles)
                {
                    files.Add(Path.GetFileName(f));
                }
            }

            comboBox.ItemsSource = files;
            if (selectItem != null && files.Contains(selectItem))
            {
                comboBox.SelectedItem = selectItem;
            }
            else if (fd.Value != null && files.Contains(fd.Value))
            {
                comboBox.SelectedItem = fd.Value;
            }
            else if (files.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        RefreshFiles();

        comboBox.SelectionChanged += (s, e) =>
        {
            if (comboBox.SelectedItem is string selectedFile)
            {
                fd.Value = selectedFile;
                var relativeVal = string.IsNullOrEmpty(fd.Folder) ? selectedFile : $"{fd.Folder}/{selectedFile}";
                SendIpcMessage(new LivelyFolderDropdown
                {
                    Name = fd.Name,
                    Value = relativeVal
                });
                SaveProperty(fd.Name, selectedFile);
            }
        };

        var browseButton = new Button
        {
            Content = "📂 Parcourir...",
            Background = Brush.Parse("#383842"),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(6)
        };

        browseButton.Click += async (s, e) =>
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider != null)
                {
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = $"Sélectionner un fichier pour {fd.Text}",
                        AllowMultiple = false,
                        FileTypeFilter = new[]
                        {
                            new FilePickerFileType("Images et Vidéos")
                            {
                                Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.webm", "*.mp4", "*.gif" }
                            },
                            new FilePickerFileType("Tous les fichiers") { Patterns = new[] { "*.*" } }
                        }
                    });

                    if (files != null && files.Count > 0)
                    {
                        var picked = files[0];
                        var srcPath = picked.Path.LocalPath;
                        var fileName = Path.GetFileName(srcPath);
                        var destPath = Path.Combine(folderPath, fileName);

                        if (!File.Exists(destPath) || !string.Equals(srcPath, destPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(srcPath, destPath, true);
                        }

                        RefreshFiles(fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error picking file: {ex.Message}");
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*, Auto"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        Grid.SetColumn(comboBox, 0);
        Grid.SetColumn(browseButton, 1);
        browseButton.Margin = new Thickness(8, 0, 0, 0);

        grid.Children.Add(comboBox);
        grid.Children.Add(browseButton);

        panel.Children.Add(grid);
    }

    private void BuildDropdownControl(DropdownModel dropdown, StackPanel panel)
    {
        if (!_defaultValues.ContainsKey(dropdown.Name))
            _defaultValues[dropdown.Name] = dropdown.Value;

        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(dropdown.Text) ? dropdown.Name : dropdown.Text,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14
        };
        panel.Children.Add(label);

        if (!string.IsNullOrEmpty(dropdown.Help))
        {
            panel.Children.Add(new TextBlock
            {
                Text = dropdown.Help,
                Foreground = Brush.Parse("#8E8E98"),
                FontSize = 11
            });
        }

        var comboBox = new ComboBox
        {
            ItemsSource = dropdown.Items,
            SelectedIndex = dropdown.Value >= 0 && dropdown.Value < (dropdown.Items?.Length ?? 0) ? dropdown.Value : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brush.Parse("#1A1A1E"),
            Foreground = Brushes.White,
            Margin = new Thickness(0, 4, 0, 0)
        };

        comboBox.SelectionChanged += (s, e) =>
        {
            var idx = comboBox.SelectedIndex;
            if (idx >= 0)
            {
                dropdown.Value = idx;
                SendIpcMessage(new LivelyDropdown
                {
                    Name = dropdown.Name,
                    Value = idx
                });
                SaveProperty(dropdown.Name, idx);
            }
        };

        panel.Children.Add(comboBox);
    }

    private void BuildCheckboxControl(CheckboxModel checkbox, StackPanel panel)
    {
        if (!_defaultValues.ContainsKey(checkbox.Name))
            _defaultValues[checkbox.Name] = checkbox.Value;

        var avaloniaCheckBox = new CheckBox
        {
            Content = string.IsNullOrEmpty(checkbox.Text) ? checkbox.Name : checkbox.Text,
            IsChecked = checkbox.Value,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14
        };

        avaloniaCheckBox.IsCheckedChanged += (s, e) =>
        {
            var isChecked = avaloniaCheckBox.IsChecked == true;
            checkbox.Value = isChecked;
            SendIpcMessage(new LivelyCheckbox
            {
                Name = checkbox.Name,
                Value = isChecked
            });
            SaveProperty(checkbox.Name, isChecked);
        };

        panel.Children.Add(avaloniaCheckBox);

        if (!string.IsNullOrEmpty(checkbox.Help))
        {
            panel.Children.Add(new TextBlock
            {
                Text = checkbox.Help,
                Foreground = Brush.Parse("#8E8E98"),
                FontSize = 11,
                Margin = new Thickness(28, 0, 0, 0)
            });
        }
    }

    private void BuildColorPickerControl(ColorPickerModel colorPicker, StackPanel panel)
    {
        if (!_defaultValues.ContainsKey(colorPicker.Name))
            _defaultValues[colorPicker.Name] = colorPicker.Value;

        var label = new TextBlock
        {
            Text = string.IsNullOrEmpty(colorPicker.Text) ? colorPicker.Name : colorPicker.Text,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14
        };
        panel.Children.Add(label);

        if (!string.IsNullOrEmpty(colorPicker.Help))
        {
            panel.Children.Add(new TextBlock
            {
                Text = colorPicker.Help,
                Foreground = Brush.Parse("#8E8E98"),
                FontSize = 11
            });
        }

        IBrush SafeParseColor(string hex)
        {
            try { return Brush.Parse(hex); }
            catch { return Brushes.Black; }
        }

        var previewBox = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(6),
            Background = SafeParseColor(colorPicker.Value ?? "#000000"),
            BorderBrush = Brush.Parse("#555560"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0)
        };

        var hexTextBox = new TextBox
        {
            Text = colorPicker.Value ?? "#000000",
            Background = Brush.Parse("#1A1A1E"),
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        void ApplyColor(string hex)
        {
            try
            {
                previewBox.Background = Brush.Parse(hex);
                colorPicker.Value = hex;
                SendIpcMessage(new LivelyColorPicker
                {
                    Name = colorPicker.Name,
                    Value = hex
                });
                SaveProperty(colorPicker.Name, hex);
            }
            catch { }
        }

        hexTextBox.PropertyChanged += (s, e) =>
        {
            if (e.Property == TextBox.TextProperty && hexTextBox.Text is string text)
            {
                if (text.StartsWith("#") && (text.Length == 7 || text.Length == 9))
                {
                    ApplyColor(text);
                }
            }
        };

        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto, *"),
            Margin = new Thickness(0, 4, 0, 4)
        };
        Grid.SetColumn(previewBox, 0);
        Grid.SetColumn(hexTextBox, 1);
        rowGrid.Children.Add(previewBox);
        rowGrid.Children.Add(hexTextBox);
        panel.Children.Add(rowGrid);

        // Preset color chips
        var swatchesPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        string[] presets = ["#000000", "#FFFFFF", "#1E1E22", "#3498DB", "#2ECC71", "#E74C3C", "#F1C40F", "#9B59B6", "#00BCD4"];
        foreach (var preset in presets)
        {
            var swatchBtn = new Button
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                Background = SafeParseColor(preset),
                BorderBrush = Brush.Parse("#444450"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(2),
                Padding = new Thickness(0)
            };
            swatchBtn.Click += (s, e) =>
            {
                hexTextBox.Text = preset;
                ApplyColor(preset);
            };
            swatchesPanel.Children.Add(swatchBtn);
        }
        panel.Children.Add(swatchesPanel);
    }

    private void BuildButtonControl(ButtonModel button, StackPanel panel)
    {
        var actionBtn = new Button
        {
            Content = string.IsNullOrEmpty(button.Text) ? button.Name : button.Text,
            Background = Brush.Parse("#383844"),
            Foreground = Brushes.White,
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        actionBtn.Click += (s, e) =>
        {
            SendIpcMessage(new LivelyButton { Name = button.Name });
        };

        panel.Children.Add(actionBtn);
    }

    private void Debounce(string key, int milliseconds, Action action)
    {
        if (_debounceTimers.TryGetValue(key, out var existingTimer))
        {
            existingTimer.Stop();
        }
        else
        {
            existingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            existingTimer.Tick += (s, e) =>
            {
                existingTimer.Stop();
                action();
            };
            _debounceTimers[key] = existingTimer;
        }
        existingTimer.Start();
    }

    private async void SendIpcMessage(IpcMessage msg)
    {
        try
        {
            var json = JsonConvert.SerializeObject(msg);
            await _client.SendMessageWallpaperAsync(new WallpaperMessageRequest
            {
                LivelyInfoPath = _info.Id,
                MonitorId = _displayId,
                Msg = json
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WallpaperPropertiesWindow] SendMessage error: {ex.Message}");
        }
    }

    private void SaveProperty(string propertyName, object value)
    {
        if (!File.Exists(_propertyFilePath))
            return;

        try
        {
            var jsonContent = File.ReadAllText(_propertyFilePath);
            // Handle comments safely
            var lines = jsonContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                                   .Where(l => !l.TrimStart().StartsWith("//"));
            var cleanJson = string.Join("\n", lines);

            var obj = JObject.Parse(cleanJson);
            if (obj[propertyName] is JObject propObj)
            {
                propObj["value"] = JToken.FromObject(value);
                File.WriteAllText(_propertyFilePath, obj.ToString(Formatting.Indented));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WallpaperPropertiesWindow] SaveProperty error: {ex.Message}");
        }
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_backupFilePath))
            {
                File.Copy(_backupFilePath, _propertyFilePath, true);
                SendIpcMessage(new LivelyButton { Name = "lively_default_settings_reload" });
                BuildControls();
            }
            else if (_defaultValues.Count > 0)
            {
                foreach (var kvp in _defaultValues)
                {
                    SaveProperty(kvp.Key, kvp.Value);
                }
                BuildControls();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Restore defaults error: {ex.Message}");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
