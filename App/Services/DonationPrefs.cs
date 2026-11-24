using System.IO;
using System.Text.Json;

namespace Mconect.App.Services;

/// <summary>Preferencias del aviso de donación, guardadas junto a la configuración de la app.</summary>
public static class DonationPrefs
{
    private static readonly string FilePath = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Mconect", "preferences.json");

    private static State _state = Load();

    public static bool Hidden => _state.Hidden;

    /// <summary>Cuenta los inicios y devuelve true cada tres lanzamientos.</summary>
    public static bool ShouldShow()
    {
        if (_state.Hidden) return false;
        _state.Launches++;
        Save();
        return _state.Launches % 3 == 0;
    }

    public static void Hide()
    {
        _state.Hidden = true;
        Save();
    }

    private static State Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State();
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        return new State();
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_state));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class State
    {
        public int Launches { get; set; }
        public bool Hidden { get; set; }
    }
}
