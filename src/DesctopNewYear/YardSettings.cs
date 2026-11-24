using System.Text.Json;

namespace DesctopNewYear;

internal sealed class YardSettings
{
    private static readonly string PathToSettings = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesctopNewYear", "yard.json");

    public int Dusk { get; set; } = 16 * 60;
    public int LightsOff { get; set; } = 23 * 60;
    public string? Monitor { get; set; }
    public bool WallpaperOnly { get; set; }
    public int Ornaments { get; set; }
    public int Snow { get; set; }

    public static YardSettings Load()
    {
        try
        {
            using var input = File.OpenRead(PathToSettings);
            var settings = JsonSerializer.Deserialize<YardSettings>(input) ?? new();
            settings.Dusk = Math.Clamp(settings.Dusk, 10 * 60, 22 * 60);
            settings.LightsOff = Math.Clamp(settings.LightsOff, settings.Dusk + 60, 1439);
            settings.Ornaments = Math.Clamp(settings.Ornaments, 0, 7);
            settings.Snow = Math.Clamp(settings.Snow, 0, 3);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public bool Save()
    {
        var temporary = PathToSettings + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathToSettings)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, PathToSettings, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal readonly record struct Daylight(int Shade, bool WindowLit, bool LanternLit)
{
    public static Daylight At(DateTime localTime, YardSettings settings)
    {
        const int dawn = 7 * 60;
        const int daylight = 9 * 60;
        var minute = localTime.Hour * 60 + localTime.Minute;
        var sunrise = Math.Clamp((minute - dawn) / (float)(daylight - dawn), 0, 1);
        var sunset = Math.Clamp((minute - settings.Dusk) / 60f, 0, 1);
        var dark = Math.Max(1 - sunrise, sunset);
        return new(
            (int)Math.Round(dark * 16),
            minute >= dawn && minute < daylight || minute >= settings.Dusk + 15 && minute < settings.LightsOff,
            minute < daylight || minute >= settings.Dusk);
    }
}

internal sealed class LightingDialog : Form
{
    private readonly DateTimePicker dusk = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true };
    private readonly DateTimePicker off = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true };

    public int Dusk => dusk.Value.Hour * 60 + dusk.Value.Minute;
    public int LightsOff => off.Value.Hour * 60 + off.Value.Minute;

    public LightingDialog(YardSettings settings)
    {
        Text = "Освещение двора";
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new(344, 172);
        dusk.Format = off.Format = DateTimePickerFormat.Custom;
        dusk.CustomFormat = off.CustomFormat = "HH:mm";
        dusk.Value = DateTime.Today.AddMinutes(settings.Dusk);
        off.Value = DateTime.Today.AddMinutes(settings.LightsOff);

        var save = new Button { Text = "Сохранить", AutoSize = true, Location = new(136, 126) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new(243, 126) };
        save.Click += (_, _) =>
        {
            if (Dusk < 600 || Dusk > 1320 || LightsOff < Dusk + 60)
            {
                MessageBox.Show(this, "Начало сумерек — с 10:00 до 22:00. Окно гаснет минимум через час, до полуночи.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
        };
        dusk.SetBounds(229, 35, 87, 25);
        off.SetBounds(229, 71, 87, 25);
        Controls.AddRange([
            new Label { Text = "По местному времени Windows", AutoSize = true, Location = new(16, 12) },
            new Label { Text = "Начало сумерек", AutoSize = true, Location = new(16, 39) },
            new Label { Text = "Выключение света в доме", AutoSize = true, Location = new(16, 75) },
            dusk, off, save, cancel
        ]);
        AcceptButton = save;
        CancelButton = cancel;
    }
}
