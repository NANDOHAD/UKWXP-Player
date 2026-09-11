using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;


    // UI文字列テーブルへの共通アクセス口。
public static class UILocalization
{
    public const string TableName = "UI";

    public static bool IsEnglish
    {
        get
        {
            try
            {
                Locale locale = LocalizationSettings.SelectedLocale;
                return locale != null
                    && locale.Identifier.Code.StartsWith("en", System.StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public static string FixedKey(string japaneseFallback)
    {
        string value = japaneseFallback ?? string.Empty;
        return "fixed." + Hash128.Compute(value).ToString().Substring(0, 16);
    }

    public static string GetFixed(string japaneseFallback, params object[] arguments)
    {
        return Get(FixedKey(japaneseFallback), japaneseFallback, arguments);
    }

    public static string Get(string entryKey, string japaneseFallback, params object[] arguments)
    {
        string template = japaneseFallback ?? string.Empty;

        if (!string.IsNullOrEmpty(entryKey))
        {
            try
            {
                // A standalone/editor caller may have no selected locale even after
                // initialization completes. Do not ask Addressables for a null-locale table.
                var settings = LocalizationSettings.GetInstanceDontCreateDefault();
                if (settings != null && LocalizationSettings.InitializationOperation.IsDone
                    && LocalizationSettings.SelectedLocale != null)
                {
                    string localized = new LocalizedString(TableName, entryKey).GetLocalizedString();
                    if (!string.IsNullOrEmpty(localized))
                        template = localized;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[UILocalization] 翻訳の取得に失敗しました ({entryKey}): {exception.Message}");
            }
        }

        if (arguments == null || arguments.Length == 0)
            return template;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, arguments);
        }
        catch (FormatException exception)
        {
            Debug.LogWarning($"[UILocalization] 翻訳文の書式が不正です ({entryKey}): {exception.Message}");
            return template;
        }
    }
}
