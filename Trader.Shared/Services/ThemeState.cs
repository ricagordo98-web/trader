namespace Trader.Shared.Services;

public enum ThemeMode
{
    Dark,
    Light,
    System
}

public sealed record AccentOption(string Key, string Label, string Dark, string Light);

public sealed class ThemeState
{
    private const string ModeKey = "pref.theme";
    private const string AccentKey = "pref.accent";

    public static readonly IReadOnlyList<AccentOption> Accents =
    [
        new("blue", "Azul", "#4C8DFF", "#2563EB"),
        new("violet", "Violeta", "#A78BFA", "#7C3AED"),
        new("teal", "Turquesa", "#2DD4BF", "#0D9488"),
        new("amber", "Âmbar", "#FBBF24", "#D97706"),
        new("rose", "Rosa", "#FB7185", "#E11D48")
    ];

    private bool _isDark = true;
    private ThemeMode _mode = ThemeMode.Dark;
    private AccentOption _accent = Accents[0];

    public event Action? Changed;

    public bool IsDark
    {
        get => _isDark;
        set
        {
            if (_isDark == value)
            {
                return;
            }

            _isDark = value;
            Changed?.Invoke();
        }
    }

    public ThemeMode Mode => _mode;

    public AccentOption Accent => _accent;

    public async Task LoadAsync(IClientStore store)
    {
        if (Enum.TryParse(await store.GetAsync(ModeKey), out ThemeMode mode))
        {
            _mode = mode;
            _isDark = mode != ThemeMode.Light;
        }

        string? accentKey = await store.GetAsync(AccentKey);
        _accent = Accents.FirstOrDefault(a => a.Key == accentKey) ?? Accents[0];
        Changed?.Invoke();
    }

    public async Task SetModeAsync(IClientStore store, ThemeMode mode, bool systemIsDark)
    {
        _mode = mode;
        _isDark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => systemIsDark
        };

        await store.SetAsync(ModeKey, mode.ToString());
        Changed?.Invoke();
    }

    public async Task SetAccentAsync(IClientStore store, AccentOption accent)
    {
        _accent = accent;
        await store.SetAsync(AccentKey, accent.Key);
        Changed?.Invoke();
    }
}
