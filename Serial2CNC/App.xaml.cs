using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace Serial2CNC
{
    public enum ThemePreference
    {
        System,
        Light,
        Dark
    }

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public ThemePreference CurrentThemePreference { get; private set; } = ThemePreference.System;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ApplyConfiguredTheme();
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            base.OnExit(e);
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (CurrentThemePreference != ThemePreference.System)
            {
                return;
            }

            Dispatcher.Invoke(ApplyConfiguredTheme);
        }

        public void SetThemePreference(ThemePreference preference)
        {
            CurrentThemePreference = preference;
            ApplyConfiguredTheme();
        }

        private void ApplyConfiguredTheme()
        {
            var isLightTheme = CurrentThemePreference switch
            {
                ThemePreference.Light => true,
                ThemePreference.Dark => false,
                _ => IsWindowsLightTheme()
            };

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isLightTheme ? BaseTheme.Light : BaseTheme.Dark);

            if (isLightTheme)
            {
                theme.SetPrimaryColor(Color.FromRgb(63, 81, 181));
                theme.SetSecondaryColor(Color.FromRgb(205, 220, 57));
            }
            else
            {
                theme.SetPrimaryColor(Color.FromRgb(149, 117, 205));
                theme.SetSecondaryColor(Color.FromRgb(129, 212, 250));
            }

            paletteHelper.SetTheme(theme);

            ApplySurfaceOverrides(isLightTheme);
        }

        private static void ApplySurfaceOverrides(bool isLightTheme)
        {
            if (Current is null)
            {
                return;
            }

            if (isLightTheme)
            {
                Current.Resources["MaterialDesign.Brush.Background"] = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
                Current.Resources["MaterialDesign.Brush.Paper"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                Current.Resources["MaterialDesign.Brush.CardBackground"] = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
                return;
            }

            Current.Resources["MaterialDesign.Brush.Background"] = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14));
            Current.Resources["MaterialDesign.Brush.Paper"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            Current.Resources["MaterialDesign.Brush.CardBackground"] = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        }

        private static bool IsWindowsLightTheme()
        {
            const string personalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            const string valueName = "AppsUseLightTheme";

            using var key = Registry.CurrentUser.OpenSubKey(personalizeKeyPath);
            if (key?.GetValue(valueName) is int value)
            {
                return value != 0;
            }

            return true;
        }
    }

}
