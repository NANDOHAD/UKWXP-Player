using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Dropdown))]
public sealed class UILanguageSelector : MonoBehaviour
{
    const string LocalePreferenceKey = "WindomXPMechaEditor.Locale";

    static readonly string[] LocaleCodes = { "ja", "en" };
    static readonly string[] LocaleNames = { "日本語", "English" };

    sealed class FixedDropdownBinding
    {
        public Dropdown dropdown;
        public string[] japaneseOptions;

        public void Refresh()
        {
            if (dropdown == null)
                return;

            int selectedIndex = dropdown.value;
            List<string> labels = new List<string>(japaneseOptions.Length);
            for (int i = 0; i < japaneseOptions.Length; i++)
                labels.Add(UILocalization.GetFixed(japaneseOptions[i]));

            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, labels.Count - 1));
            dropdown.RefreshShownValue();
        }
    }

    Dropdown languageDropdown;
    readonly List<FixedDropdownBinding> fixedDropdowns = new List<FixedDropdownBinding>();

    IEnumerator Start()
    {
        languageDropdown = GetComponent<Dropdown>();
        yield return LocalizationSettings.InitializationOperation;

        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(new List<string>(LocaleNames));
        BindFixedDropdowns();

        string code = WindomToolSettings.Load().languageCode;
        if (string.IsNullOrEmpty(code))
            code = PlayerPrefs.GetString(LocalePreferenceKey, string.Empty);
        if (string.IsNullOrEmpty(code))
            code = Application.systemLanguage == SystemLanguage.English ? "en" : "ja";

        int selectedIndex = code == "en" ? 1 : 0;
        languageDropdown.SetValueWithoutNotify(selectedIndex);
        ApplyLocale(selectedIndex, false);
        languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    void OnDestroy()
    {
        if (languageDropdown != null)
            languageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void OnLanguageChanged(int index)
    {
        ApplyLocale(index, true);
    }

    void OnLocaleChanged(Locale locale)
    {
        RefreshLocalizedDropdowns();
    }

    public void RefreshLocalizedDropdowns()
    {
        if (fixedDropdowns.Count == 0)
            BindFixedDropdowns();
        RefreshFixedDropdowns();
    }

    void BindFixedDropdowns()
    {
        fixedDropdowns.Clear();
        Dropdown[] candidates = Resources.FindObjectsOfTypeAll<Dropdown>();
        for (int i = 0; i < candidates.Length; i++)
        {
            Dropdown candidate = candidates[i];
            if (candidate == null || candidate == languageDropdown
                || candidate.gameObject.scene != gameObject.scene)
                continue;

            string[] japaneseOptions;
            if (!TryGetFixedOptions(candidate, out japaneseOptions))
                continue;

            fixedDropdowns.Add(new FixedDropdownBinding
            {
                dropdown = candidate,
                japaneseOptions = japaneseOptions
            });
        }
    }

    static bool TryGetFixedOptions(Dropdown dropdown, out string[] japaneseOptions)
    {
        japaneseOptions = null;
        if (dropdown.options == null)
            return false;

        string[] options = new string[dropdown.options.Count];
        for (int i = 0; i < dropdown.options.Count; i++)
            options[i] = dropdown.options[i].text;

        if (Matches(options, "位置", "回転", "大きさ")
            || Matches(options, "ワールド", "ローカル")
            || Matches(options, "編集モード", "プレビューモード","テストモード"))
        {
            japaneseOptions = options;
            return true;
        }

        return false;
    }

    static bool Matches(string[] actual, params string[] expected)
    {
        if (actual.Length != expected.Length)
            return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(actual[i], expected[i], System.StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    void RefreshFixedDropdowns()
    {
        for (int i = 0; i < fixedDropdowns.Count; i++)
            fixedDropdowns[i].Refresh();
    }

    void ApplyLocale(int index, bool savePreference)
    {
        if (index < 0 || index >= LocaleCodes.Length)
            index = 0;

        Locale locale = LocalizationSettings.AvailableLocales.GetLocale(LocaleCodes[index]);
        if (locale == null)
        {
            Debug.LogWarning($"[UILanguageSelector] Localeが見つかりません: {LocaleCodes[index]}");
            return;
        }

        LocalizationSettings.SelectedLocale = locale;
        if (savePreference)
        {
            WindomToolSettings.SaveLanguage(LocaleCodes[index]);
            PlayerPrefs.SetString(LocalePreferenceKey, LocaleCodes[index]);
            PlayerPrefs.Save();
        }
    }
}
