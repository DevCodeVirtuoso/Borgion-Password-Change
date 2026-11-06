using System;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BorgionPasswordChange
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool LockWorkStation();

        UserInfo _user;
        PasswordPolicy _policy = new PasswordPolicy();
        bool _accountUsable = true;
        bool _busy;
        bool _syncing;

        public MainWindow()
        {
            InitializeComponent();
            MaxHeight = SystemParameters.WorkArea.Height; // kleine schermen: kaart scrollt

            HelpdeskText.Text = ConfigurationManager.AppSettings["HelpdeskText"] ?? "";
            var v = typeof(MainWindow).Assembly.GetName().Version;
            VersionText.Text = $"v{v.Major}.{v.Minor}.{v.Build}";
            if (int.TryParse(ConfigurationManager.AppSettings["FallbackMinLength"], out var fb) && fb > 0)
                _policy.MinLength = fb;

            Loaded += async (_, __) => { PwCurrent.Focus(); await LoadUserAsync(); };
            PreviewKeyDown += (_, __) => UpdateCapsLock();
            Activated += (_, __) => UpdateCapsLock();
            UpdateRules();
        }

        // ------------------------------------------------------------------ account

        async Task LoadUserAsync()
        {
            UserText.Text = $"Aangemeld als {Environment.UserDomainName}\\{Environment.UserName}";

            if (AdService.IsLocalAccount())
            {
                _accountUsable = false;
                SetInfo("Je bent aangemeld met een lokaal account. Deze app werkt alleen voor domeinaccounts.", error: true);
                UpdateRules();
                return;
            }

            try
            {
                var fallback = _policy.MinLength;
                _user = await Task.Run(() => AdService.LoadCurrentUser(fallback));
                _policy = _user.Policy;
                UserText.Text = $"Aangemeld als {_user.DisplayName}  ({_user.Domain}\\{_user.SamAccountName})";
                SetInfo(ExpiryText(_user), error: false);
            }
            catch (AdException ex)
            {
                // Toch laten proberen: de domeincontroller geeft dan een duidelijke foutmelding.
                SetInfo(ex.Message, error: true);
            }
            UpdateRules();
        }

        static string ExpiryText(UserInfo u)
        {
            if (u.PasswordNeverExpires || u.PasswordExpires == null)
                return "Je wachtwoord verloopt niet automatisch.";
            var days = (int)Math.Floor((u.PasswordExpires.Value - DateTime.Now).TotalDays);
            var date = u.PasswordExpires.Value.ToString("dddd d MMMM yyyy 'om' HH:mm", new System.Globalization.CultureInfo("nl-NL"));
            if (days < 0) return "Je wachtwoord is verlopen. Wijzig het nu.";
            if (days == 0) return $"Je wachtwoord verloopt vandaag ({date}).";
            if (days == 1) return $"Je wachtwoord verloopt morgen ({date}).";
            return $"Je wachtwoord verloopt over {days} dagen, op {date}.";
        }

        void SetInfo(string text, bool error)
        {
            InfoText.Text = text;
            InfoBanner.Background = (Brush)FindResource(error ? "DangerSoftBrush" : "AccentSoftBrush");
            InfoText.Foreground = (Brush)FindResource(error ? "DangerBrush" : "TextBrush");
        }

        // ------------------------------------------------------------------ invoer

        string Current => ShowToggle.IsChecked == true ? TxtCurrent.Text : PwCurrent.Password;
        string New => ShowToggle.IsChecked == true ? TxtNew.Text : PwNew.Password;
        string Confirm => ShowToggle.IsChecked == true ? TxtConfirm.Text : PwConfirm.Password;

        void Pw_Changed(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            ErrorBanner.Visibility = Visibility.Collapsed;
            UpdateRules();
        }

        void Txt_Changed(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            ErrorBanner.Visibility = Visibility.Collapsed;
            UpdateRules();
        }

        void ShowToggle_Changed(object sender, RoutedEventArgs e)
        {
            var show = ShowToggle.IsChecked == true;
            _syncing = true;
            Sync(PwCurrent, TxtCurrent, show);
            Sync(PwNew, TxtNew, show);
            Sync(PwConfirm, TxtConfirm, show);
            _syncing = false;
            ShowToggle.Content = show ? "Wachtwoorden verbergen" : "Wachtwoorden tonen";
            UpdateRules();
        }

        static void Sync(PasswordBox pb, TextBox tb, bool show)
        {
            var focused = pb.IsKeyboardFocusWithin || tb.IsKeyboardFocusWithin;
            if (show) { tb.Text = pb.Password; pb.Clear(); }
            else { pb.Password = tb.Text; tb.Clear(); }
            pb.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            tb.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (focused)
            {
                if (show) { tb.Focus(); tb.CaretIndex = tb.Text.Length; }
                else pb.Focus();
            }
        }

        void UpdateCapsLock()
        {
            CapsText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ eisen

        void UpdateRules()
        {
            var pw = New;
            var hasNew = pw.Length > 0;
            var hasConfirm = Confirm.Length > 0;

            var lengthOk = PasswordRules.LengthOk(pw, _policy);
            var complexOk = PasswordRules.ComplexityOk(pw, _policy);
            var nameOk = !_policy.Complexity || PasswordRules.NameOk(pw, _user);
            var differentOk = hasNew && pw != Current;
            var matchOk = hasNew && pw == Confirm;

            SetRule(RuleLength, lengthOk, hasNew, $"is minimaal {_policy.MinLength} tekens lang");
            if (_policy.Complexity)
            {
                SetRule(RuleComplex, complexOk, hasNew, "bevat 3 van de 4: hoofdletter, kleine letter, cijfer, leesteken");
                SetRule(RuleName, nameOk, hasNew, "bevat niet je gebruikersnaam of (delen van) je naam");
                RuleComplex.Visibility = RuleName.Visibility = Visibility.Visible;
            }
            else
            {
                RuleComplex.Visibility = RuleName.Visibility = Visibility.Collapsed;
            }
            SetRule(RuleDifferent, differentOk, hasNew && Current.Length > 0, "is anders dan je huidige wachtwoord");
            SetRule(RuleMatch, matchOk, hasConfirm, "is twee keer hetzelfde ingevuld");

            if (_policy.History > 0)
            {
                RuleHistory.Text = $"•  mag niet een van je laatste {_policy.History} wachtwoorden zijn";
                RuleHistory.Visibility = Visibility.Visible;
            }

            // Sterkte
            var strength = PasswordRules.Strength(pw);
            var colors = new[] { "LineBrush", "DangerBrush", "AccentBrush", "SuccessBrush", "SuccessBrush" };
            var labels = new[] { "", "Zwak", "Redelijk", "Goed", "Sterk" };
            var bars = new[] { Bar1, Bar2, Bar3, Bar4 };
            for (int i = 0; i < bars.Length; i++)
                bars[i].Background = (Brush)FindResource(i < strength ? colors[strength] : "LineBrush");
            StrengthText.Text = labels[strength];

            BtnChange.IsEnabled = !_busy && _accountUsable && Current.Length > 0 &&
                                  lengthOk && complexOk && nameOk && differentOk && matchOk;
        }

        void SetRule(TextBlock tb, bool ok, bool active, string text)
        {
            if (!active)
            {
                tb.Text = "•  " + text;
                tb.Foreground = (Brush)FindResource("MutedBrush");
            }
            else if (ok)
            {
                tb.Text = "✓  " + text;
                tb.Foreground = (Brush)FindResource("SuccessBrush");
            }
            else
            {
                tb.Text = "✗  " + text;
                tb.Foreground = (Brush)FindResource("DangerBrush");
            }
        }

        // ------------------------------------------------------------------ acties

        async void Change_Click(object sender, RoutedEventArgs e)
        {
            if (!BtnChange.IsEnabled) return;
            var current = Current;
            var pw = New;

            SetBusy(true);
            ErrorBanner.Visibility = Visibility.Collapsed;
            try
            {
                await Task.Run(() => AdService.ChangePassword(current, pw));
                ClearFields();
                FormPanel.Visibility = Visibility.Collapsed;
                DonePanel.Visibility = Visibility.Visible;
                BtnLock.Focus();
            }
            catch (AdException ex)
            {
                ErrorText.Text = ex.Message;
                ErrorBanner.Visibility = Visibility.Visible;
                if (ex.Message.StartsWith("Je huidige wachtwoord"))
                {
                    ClearBox(PwCurrent, TxtCurrent);
                    (ShowToggle.IsChecked == true ? (Control)TxtCurrent : PwCurrent).Focus();
                }
            }
            finally
            {
                SetBusy(false);
            }
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            BusyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement c in new UIElement[] { PwCurrent, PwNew, PwConfirm, TxtCurrent, TxtNew, TxtConfirm, ShowToggle, BtnCancel })
                c.IsEnabled = !busy;
            Cursor = busy ? Cursors.Wait : null;
            UpdateRules();
        }

        void ClearFields()
        {
            _syncing = true;
            ClearBox(PwCurrent, TxtCurrent);
            ClearBox(PwNew, TxtNew);
            ClearBox(PwConfirm, TxtConfirm);
            _syncing = false;
        }

        static void ClearBox(PasswordBox pb, TextBox tb)
        {
            pb.Clear();
            tb.Clear();
        }

        void Lock_Click(object sender, RoutedEventArgs e)
        {
            LockWorkStation();
            Close();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_busy) { e.Cancel = true; return; }
            ClearFields();
            base.OnClosing(e);
        }
    }
}
