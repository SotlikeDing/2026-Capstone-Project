using Microsoft.JSInterop;

namespace WebApp.Services
{
    /// <summary>
    /// Scoped (per-circuit) light/dark theme state for the Blazor Server app. Persisted
    /// client-side via a JS-interop cookie helper ("themeCookie", see wwwroot JS) rather
    /// than server-side storage, so the theme survives a page reload/new circuit without
    /// needing a signed-in user. Components subscribe to OnThemeChanged to re-render
    /// when the theme flips.
    /// </summary>
    public class ThemeService
    {
        public string CurrentTheme { get; private set; } = "light"; // Default to "light" (Savanna Sunrise)

        public event Action? OnThemeChanged;

        /// <summary>Reads the saved theme cookie on app startup (MainLayout) and applies it; silently keeps the "light" default if JS interop fails or no cookie is set yet.</summary>
        public async Task InitializeThemeAsync(IJSRuntime js)
        {
            try
            {
                var savedTheme = await js.InvokeAsync<string>("themeCookie.get", "mikpunt_theme");
                if (!string.IsNullOrEmpty(savedTheme) && (savedTheme == "light" || savedTheme == "dark"))
                {
                    if (CurrentTheme != savedTheme)
                    {
                        CurrentTheme = savedTheme;
                        OnThemeChanged?.Invoke();
                    }
                }
            }
            catch { }
        }

        /// <summary>Flips the theme, persists it to the cookie via JS interop (1-year expiry), and notifies subscribers. Prefer this over ToggleTheme when an IJSRuntime is available so the choice survives a reload.</summary>
        public async Task ToggleThemeAsync(IJSRuntime js)
        {
            CurrentTheme = CurrentTheme == "light" ? "dark" : "light";
            try
            {
                await js.InvokeVoidAsync("themeCookie.set", "mikpunt_theme", CurrentTheme, 365);
            }
            catch { }
            OnThemeChanged?.Invoke();
        }

        /// <summary>Sets the theme directly (no-op for any value other than "light"/"dark"). Does not persist to the cookie - callers needing persistence should use ToggleThemeAsync or set the cookie themselves.</summary>
        public void SetTheme(string theme)
        {
            if (theme == "dark" || theme == "light")
            {
                CurrentTheme = theme;
                OnThemeChanged?.Invoke();
            }
        }

        /// <summary>Synchronous flip with no cookie persistence - used where an IJSRuntime isn't conveniently available; the choice will not survive a reload.</summary>
        public void ToggleTheme()
        {
            CurrentTheme = CurrentTheme == "light" ? "dark" : "light";
            OnThemeChanged?.Invoke();
        }

        /// <summary>Hero background image for the current theme.</summary>
        public string BackgroundImageUrl => CurrentTheme == "light"
            ? "images/savanna_sunrise.jpg"
            : "images/bosveld_sterrenag.jpg";

        /// <summary>Display name (Afrikaans) for the current theme, shown in the UI's theme toggle.</summary>
        public string ThemeName => CurrentTheme == "light"
            ? "Savanna Sunburst (Sonsopkoms)"
            : "Bosveld Sterrenag (Nagmaus)";
    }
}
