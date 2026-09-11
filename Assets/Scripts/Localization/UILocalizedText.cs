using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Text))]
public sealed class UILocalizedText : MonoBehaviour
{
    [SerializeField] string entryKey = string.Empty;

    Text targetText;
    LocalizedString localizedString;

    public string EntryKey
    {
        get { return entryKey; }
    }

    public void SetEntryKey(string value)
    {
        if (entryKey == value)
            return;

        Unbind();
        entryKey = value ?? string.Empty;
        if (isActiveAndEnabled)
            Bind();
    }

    void OnEnable()
    {
        Bind();
    }

    void OnDisable()
    {
        Unbind();
    }

    void Bind()
    {
        if (string.IsNullOrEmpty(entryKey) || LocalizationSettings.GetInstanceDontCreateDefault() == null)
            return;

        if (targetText == null)
            targetText = GetComponent<Text>();

        localizedString = new LocalizedString(UILocalization.TableName, entryKey);
        localizedString.StringChanged += UpdateText;
    }

    void Unbind()
    {
        if (localizedString != null)
            localizedString.StringChanged -= UpdateText;

        localizedString = null;
    }

    void UpdateText(string value)
    {
        if (targetText == null)
            targetText = GetComponent<Text>();

        targetText.text = value;
    }
}
