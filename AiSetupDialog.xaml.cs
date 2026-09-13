using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class AiSetupDialog : Window
{
    public bool UseOffline { get; private set; }

    public AiSetupDialog()
    {
        InitializeComponent();
        ThemeChrome.Apply(this);
        foreach (var p in AiProviders.All)
            ProviderBox.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        SelectId(PrivacyStore.Settings.AiProvider);
        if (!string.IsNullOrWhiteSpace(PrivacyStore.Settings.AiBaseUrl))
            EndpointBox.Text = PrivacyStore.Settings.AiBaseUrl;
        ConsentBox.IsChecked = PrivacyStore.Consent.CloudAiConsent;
        RefreshHint();
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e) => RefreshHint();

    private void RefreshHint()
    {
        var def = Current();
        HintText.Text = def.Hint;
        var showUrl = def.NeedsEndpoint;
        EndpointLabel.Visibility = EndpointBox.Visibility = showUrl ? Visibility.Visible : Visibility.Collapsed;
    }

    private AiProviderDef Current()
    {
        var id = (ProviderBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return AiProviders.Find(id);
    }

    private void SelectId(string id)
    {
        foreach (var item in ProviderBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, id, StringComparison.OrdinalIgnoreCase))
            {
                ProviderBox.SelectedItem = item;
                return;
            }
        }
        if (ProviderBox.Items.Count > 0)
            ProviderBox.SelectedIndex = 0;
    }

    private void OnHintClick(object sender, RoutedEventArgs e) => OpenHint();
    private void OnHintTextClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenHint();

    private void OpenHint()
    {
        var url = Current().KeyUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void OnOffline(object sender, RoutedEventArgs e)
    {
        UseOffline = true;
        DialogResult = true;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var def = Current();
        var key = KeyBox.Text.Trim();
        var endpoint = EndpointBox.Text.Trim();

        if (AiProviders.IsGoogleBlocked(def.Id, endpoint, def.ChatUrl))
        {
            MessageBox.Show("Google / Gemini is not allowed in DeGoogle Kit.", "Blocked");
            return;
        }

        if (def.NeedsEndpoint && string.IsNullOrWhiteSpace(endpoint))
        {
            MessageBox.Show("Paste an OpenAI-compatible URL (Ollama, OpenClaw, LM Studio, LiteLLM).", "Endpoint");
            return;
        }

        if (!def.AllowMissingKey && key.Length < 8)
        {
            MessageBox.Show("Paste an API key from " + def.Name + " first.", "API key");
            return;
        }

        if (ConsentBox.IsChecked != true)
        {
            MessageBox.Show("Tick consent. GDPR needs a clear yes before a question leaves this PC.", "Consent");
            return;
        }

        if (key.Length >= 8 && !AskAccess.For(this, AccessKind.StoreSecret,
                "Save this API key for your Windows user (DPAPI)? It is never sent to us or to Google by this save."))
            return;

        var settings = PrivacyStore.Settings;
        settings.AiProvider = def.Id;
        settings.AiModel = AiProviders.NormalizeModel(def.Id, def.DefaultModel);
        settings.AiBaseUrl = def.NeedsEndpoint ? endpoint : "";
        PrivacyStore.SaveSettings(settings);
        if (key.Length >= 8)
            SecretStore.SaveApiKey(key);
        PrivacyStore.SetCloudAiConsent(true);
        UseOffline = false;
        DialogResult = true;
        Close();
    }
}
