﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Infrastructure.Logger;
using Flow.Launcher.Infrastructure.UserSettings;

using static Flow.Launcher.Core.Resource.Theme.ParameterTypes;

namespace Flow.Launcher.Core.Resource
{
    public class Theme
    {
        private const int ShadowExtraMargin = 32;

        private readonly List<string> _themeDirectories = new List<string>();
        private ResourceDictionary _oldResource;
        private string _oldTheme;
        public Settings Settings { get; set; }
        private const string Folder = Constant.Themes;
        private const string Extension = ".xaml";
        private string DirectoryPath => Path.Combine(Constant.ProgramDirectory, Folder);
        private string UserDirectoryPath => Path.Combine(DataLocation.DataDirectory(), Folder);
        public bool BlurEnabled { get; set; }

        private double mainWindowWidth;
        private Func<bool> _isDarkTheme;

        public Theme(Func<bool> isDarkTheme)
        {
            _isDarkTheme = isDarkTheme;
            _themeDirectories.Add(DirectoryPath);
            _themeDirectories.Add(UserDirectoryPath);
            MakeSureThemeDirectoriesExist();

            var dicts = Application.Current.Resources.MergedDictionaries;
            _oldResource = dicts.First(d =>
            {
                if (d.Source == null)
                    return false;

                var p = d.Source.AbsolutePath;
                var dir = Path.GetDirectoryName(p).NonNull();
                var info = new DirectoryInfo(dir);
                var f = info.Name;
                var e = Path.GetExtension(p);
                var found = f == Folder && e == Extension;
                return found;
            });
            _oldTheme = Path.GetFileNameWithoutExtension(_oldResource.Source.AbsolutePath);
        }

        private void MakeSureThemeDirectoriesExist()
        {
            foreach (var dir in _themeDirectories.Where(dir => !Directory.Exists(dir)))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                }
                catch (Exception e)
                {
                    Log.Exception($"|Theme.MakesureThemeDirectoriesExist|Exception when create directory <{dir}>", e);
                }
            }
        }

        public bool ChangeTheme(string theme)
        {
            const string defaultTheme = Constant.DefaultTheme;

            string path = GetThemePath(theme);
            try
            {
                if (string.IsNullOrEmpty(path))
                    throw new DirectoryNotFoundException("Theme path can't be found <{path}>");
                
                // reload all resources even if the theme itself hasn't changed in order to pickup changes
                // to things like fonts
                UpdateResourceDictionary(GetResourceDictionary(theme));
                
                Settings.Theme = theme;

                
                //always allow re-loading default theme, in case of failure of switching to a new theme from default theme
                if (_oldTheme != theme || theme == defaultTheme)
                {
                    _oldTheme = Path.GetFileNameWithoutExtension(_oldResource.Source.AbsolutePath);
                }

                BlurEnabled = IsBlurTheme();
                SetBlurForWindow();

                if (Settings.UseDropShadowEffect && BlurEnabled == false)
                    AddDropShadowEffectToCurrentTheme();


            }
            catch (DirectoryNotFoundException)
            {
                Log.Error($"|Theme.ChangeTheme|Theme <{theme}> path can't be found");
                if (theme != defaultTheme)
                {
                    MessageBox.Show(string.Format(InternationalizationManager.Instance.GetTranslation("theme_load_failure_path_not_exists"), theme));
                    ChangeTheme(defaultTheme);
                }
                return false;
            }
            catch (XamlParseException)
            {
                Log.Error($"|Theme.ChangeTheme|Theme <{theme}> fail to parse");
                if (theme != defaultTheme)
                {
                    MessageBox.Show(string.Format(InternationalizationManager.Instance.GetTranslation("theme_load_failure_parse_error"), theme));
                    ChangeTheme(defaultTheme);
                }
                return false;
            }
            return true;
        }

        private void UpdateResourceDictionary(ResourceDictionary dictionaryToUpdate)
        {
            var dicts = Application.Current.Resources.MergedDictionaries;

            dicts.Remove(_oldResource);
            dicts.Add(dictionaryToUpdate);
            _oldResource = dictionaryToUpdate;
        }

        private ResourceDictionary GetThemeResourceDictionary(string theme)
        {
            var uri = GetThemePath(theme);
            var dict = new ResourceDictionary
            {
                Source = new Uri(uri, UriKind.Absolute)
            };

            return dict;
        }

        private ResourceDictionary CurrentThemeResourceDictionary() => GetThemeResourceDictionary(Settings.Theme);

        public ResourceDictionary GetResourceDictionary(string theme)
        {
            var dict = GetThemeResourceDictionary(theme);
            
            if (dict["QueryBoxStyle"] is Style queryBoxStyle &&
                dict["QuerySuggestionBoxStyle"] is Style querySuggestionBoxStyle)
            {
                var fontFamily = new FontFamily(Settings.QueryBoxFont);
                var fontStyle = FontHelper.GetFontStyleFromInvariantStringOrNormal(Settings.QueryBoxFontStyle);
                var fontWeight = FontHelper.GetFontWeightFromInvariantStringOrNormal(Settings.QueryBoxFontWeight);
                var fontStretch = FontHelper.GetFontStretchFromInvariantStringOrNormal(Settings.QueryBoxFontStretch);

                queryBoxStyle.Setters.Add(new Setter(TextBox.FontFamilyProperty, fontFamily));
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontStyleProperty, fontStyle));
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontWeightProperty, fontWeight));
                queryBoxStyle.Setters.Add(new Setter(TextBox.FontStretchProperty, fontStretch));

                var caretBrushPropertyValue = queryBoxStyle.Setters.OfType<Setter>().Any(x => x.Property.Name == "CaretBrush");
                var foregroundPropertyValue = queryBoxStyle.Setters.OfType<Setter>().Where(x => x.Property.Name == "Foreground")
                    .Select(x => x.Value).FirstOrDefault();
                if (!caretBrushPropertyValue && foregroundPropertyValue != null) //otherwise BaseQueryBoxStyle will handle styling
                    queryBoxStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, foregroundPropertyValue));

                // Query suggestion box's font style is aligned with query box
                querySuggestionBoxStyle.Setters.Add(new Setter(TextBox.FontFamilyProperty, fontFamily));
                querySuggestionBoxStyle.Setters.Add(new Setter(TextBox.FontStyleProperty, fontStyle));
                querySuggestionBoxStyle.Setters.Add(new Setter(TextBox.FontWeightProperty, fontWeight));
                querySuggestionBoxStyle.Setters.Add(new Setter(TextBox.FontStretchProperty, fontStretch));
            }

            if (dict["ItemTitleStyle"] is Style resultItemStyle &&
                dict["ItemSubTitleStyle"] is Style resultSubItemStyle &&
                dict["ItemSubTitleSelectedStyle"] is Style resultSubItemSelectedStyle &&
                dict["ItemTitleSelectedStyle"] is Style resultItemSelectedStyle &&
                dict["ItemHotkeyStyle"] is Style resultHotkeyItemStyle &&
                dict["ItemHotkeySelectedStyle"] is Style resultHotkeyItemSelectedStyle)
            {
                Setter fontFamily = new Setter(TextBlock.FontFamilyProperty, new FontFamily(Settings.ResultFont));
                Setter fontStyle = new Setter(TextBlock.FontStyleProperty, FontHelper.GetFontStyleFromInvariantStringOrNormal(Settings.ResultFontStyle));
                Setter fontWeight = new Setter(TextBlock.FontWeightProperty, FontHelper.GetFontWeightFromInvariantStringOrNormal(Settings.ResultFontWeight));
                Setter fontStretch = new Setter(TextBlock.FontStretchProperty, FontHelper.GetFontStretchFromInvariantStringOrNormal(Settings.ResultFontStretch));

                Setter[] setters = { fontFamily, fontStyle, fontWeight, fontStretch };
                Array.ForEach(
                    new[] { resultItemStyle, resultSubItemStyle, resultItemSelectedStyle, resultSubItemSelectedStyle, resultHotkeyItemStyle, resultHotkeyItemSelectedStyle }, o 
                    => Array.ForEach(setters, p => o.Setters.Add(p)));
            }
            /* Ignore Theme Window Width and use setting */
            var windowStyle = dict["WindowStyle"] as Style;
            var width = Settings.WindowSize;
            windowStyle.Setters.Add(new Setter(Window.WidthProperty, width));
            mainWindowWidth = (double)width;
            return dict;
        }

        private ResourceDictionary GetCurrentResourceDictionary( )
        {
            return  GetResourceDictionary(Settings.Theme);
        }

        public List<string> LoadAvailableThemes()
        {
            List<string> themes = new List<string>();
            foreach (var themeDirectory in _themeDirectories)
            {
                themes.AddRange(
                    Directory.GetFiles(themeDirectory)
                        .Where(filePath => filePath.EndsWith(Extension) && !filePath.EndsWith("Base.xaml"))
                        .ToList());
            }
            return themes.OrderBy(o => o).ToList();
        }

        private string GetThemePath(string themeName)
        {
            foreach (string themeDirectory in _themeDirectories)
            {
                string path = Path.Combine(themeDirectory, themeName + Extension);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        public void AddDropShadowEffectToCurrentTheme()
        {
            var dict = GetCurrentResourceDictionary();

            var windowBorderStyle = dict["WindowBorderStyle"] as Style;

            var effectSetter = new Setter
            {
                Property = Border.EffectProperty,
                Value = new DropShadowEffect
                {
                    Opacity = 0.3,
                    ShadowDepth = 12,
                    Direction = 270,
                    BlurRadius = 30
                }
            };

            var marginSetter = windowBorderStyle.Setters.FirstOrDefault(setterBase => setterBase is Setter setter && setter.Property == Border.MarginProperty) as Setter;
            if (marginSetter == null)
            {
                marginSetter = new Setter()
                {
                    Property = Border.MarginProperty,
                    Value = new Thickness(ShadowExtraMargin, 12, ShadowExtraMargin, ShadowExtraMargin),
                };
                windowBorderStyle.Setters.Add(marginSetter);
            }
            else
            {
                var baseMargin = (Thickness)marginSetter.Value;
                var newMargin = new Thickness(
                    baseMargin.Left + ShadowExtraMargin,
                    baseMargin.Top + ShadowExtraMargin,
                    baseMargin.Right + ShadowExtraMargin,
                    baseMargin.Bottom + ShadowExtraMargin);
                marginSetter.Value = newMargin;
            }

            windowBorderStyle.Setters.Add(effectSetter);
            UpdateResourceDictionary(dict);
        }

        public void RemoveDropShadowEffectFromCurrentTheme()
        {
            var dict = GetCurrentResourceDictionary();
            var windowBorderStyle = dict["WindowBorderStyle"] as Style;

            var effectSetter = windowBorderStyle.Setters.FirstOrDefault(setterBase => setterBase is Setter setter && setter.Property == Border.EffectProperty) as Setter;
            var marginSetter = windowBorderStyle.Setters.FirstOrDefault(setterBase => setterBase is Setter setter && setter.Property == Border.MarginProperty) as Setter;

            if (effectSetter != null)
            {
                windowBorderStyle.Setters.Remove(effectSetter);
            }
            if (marginSetter != null)
            {
                var currentMargin = (Thickness)marginSetter.Value;
                var newMargin = new Thickness(
                    currentMargin.Left - ShadowExtraMargin,
                    currentMargin.Top - ShadowExtraMargin,
                    currentMargin.Right - ShadowExtraMargin,
                    currentMargin.Bottom - ShadowExtraMargin);
                marginSetter.Value = newMargin;
            }

            UpdateResourceDictionary(dict);
        }


        #region Blur Handling
        public class ParameterTypes
        {

            [Flags]
            public enum DWMWINDOWATTRIBUTE
            {
                DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
                DWMWA_SYSTEMBACKDROP_TYPE = 38,
                DWMWA_TRANSITIONS_FORCEDISABLED = 3,
                DWMWA_BORDER_COLOR
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MARGINS
            {
                public int cxLeftWidth;      // width of left border that retains its size
                public int cxRightWidth;     // width of right border that retains its size
                public int cyTopHeight;      // height of top border that retains its size
                public int cyBottomHeight;   // height of bottom border that retains its size
            };
        }

        public static class Methods
        {
            [DllImport("DwmApi.dll")]
            static extern int DwmExtendFrameIntoClientArea(
                IntPtr hwnd,
                ref ParameterTypes.MARGINS pMarInset);

            [DllImport("dwmapi.dll")]
            static extern int DwmSetWindowAttribute(IntPtr hwnd, ParameterTypes.DWMWINDOWATTRIBUTE dwAttribute, ref int pvAttribute, int cbAttribute);

            public static int ExtendFrame(IntPtr hwnd, ParameterTypes.MARGINS margins)
                => DwmExtendFrameIntoClientArea(hwnd, ref margins);

            public static int SetWindowAttribute(IntPtr hwnd, ParameterTypes.DWMWINDOWATTRIBUTE attribute, int parameter)
                => DwmSetWindowAttribute(hwnd, attribute, ref parameter, Marshal.SizeOf<int>());
        }

        Window mainWindow = Application.Current.MainWindow;

        public void RefreshFrame()
        {
            IntPtr mainWindowPtr = new WindowInteropHelper(mainWindow).Handle;
            HwndSource mainWindowSrc = HwndSource.FromHwnd(mainWindowPtr);
            //mainWindowSrc.CompositionTarget.BackgroundColor = Color.FromArgb(0, 255, 181, 178);

            ParameterTypes.MARGINS margins = new ParameterTypes.MARGINS();
            margins.cxLeftWidth = -1;
            margins.cxRightWidth = -1;
            margins.cyTopHeight = -1;
            margins.cyBottomHeight = -1;
            Methods.ExtendFrame(mainWindowSrc.Handle, margins);

            // Remove OS minimizing/maximizing animation
            Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_TRANSITIONS_FORCEDISABLED, 3);
            Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, 0x00FF0000);

            SetBlurForWindow();
        }



        /// <summary>
        /// Sets the blur for a window via SetWindowCompositionAttribute
        /// </summary>
        public void SetBlurForWindow()
        {
            //SetWindowAccent();
            var dict = GetThemeResourceDictionary(Settings.Theme);
            var windowBorderStyle = dict["WindowBorderStyle"] as Style;
            if (BlurEnabled)
            {
                windowBorderStyle.Setters.Remove(windowBorderStyle.Setters.OfType<Setter>().FirstOrDefault(x => x.Property.Name == "Background"));
                windowBorderStyle.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
                mainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, 3);
                BlurColor(BlurMode());
            }
            else
            {
                mainWindow.WindowStyle = WindowStyle.None;
                if (windowBorderStyle.Setters.OfType<Setter>().FirstOrDefault(x => x.Property.Name == "Background") != null)
                {
                    windowBorderStyle.Setters.Add(windowBorderStyle.Setters.OfType<Setter>().FirstOrDefault(x => x.Property.Name == "Background"));
                }
                Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, 1);
            }
            UpdateResourceDictionary(dict);
        }

        public void BlurColor(string Color)
        {
            if (Color == "Light")
            {
                Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, 0);
            }
            else if (Color == "Dark")
            {
                Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
            }
            else /* Case of "Auto" Blur Type Theme */
            {
                if (_isDarkTheme())
                {
                    Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
                }
                else
                {
                    Methods.SetWindowAttribute(new WindowInteropHelper(mainWindow).Handle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, 0);
                }
            }

        }
        public bool IsBlurTheme()
        {
            if (Environment.OSVersion.Version >= new Version(6, 2))
            {
                var resource = Application.Current.TryFindResource("ThemeBlurEnabled");

                if (resource is bool)
                    return (bool)resource;

                return false;
            }

            return false;
        }
        public string BlurMode()
        {
            if (Environment.OSVersion.Version >= new Version(6, 2))
            {
                var resource = Application.Current.TryFindResource("BlurMode");

                if (resource is string)
                    return (string)resource;

                return null;
            }

            return null;
        }

        #endregion
    }
}
