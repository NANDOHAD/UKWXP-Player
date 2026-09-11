using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public delegate void call(string rtext);
public class UI_InputBox : MonoBehaviour
{
    public Text text;
    public InputField input;
    public call _callBack;
    private call _cancelCallback;
    public Dropdown addPartsList;
    private bool isDropdownMode = false;
    private List<string> optionValues;

    // Start is called before the first frame update
    void Start()
    {
        //gameObject.SetActive(false);
    }

    public void openDialog(string message, string defaultText, call callback)
    {
        _cancelCallback = null;
        isDropdownMode = false;
        optionValues = null;
        text.text = message;
        input.text = defaultText;
        _callBack = callback;
        if (input != null)
            input.gameObject.SetActive(true);
        if (addPartsList != null)
            addPartsList.gameObject.SetActive(false);
        gameObject.SetActive(true);
    }

    public void openSelectDialog(string message, string defaultText, call callback)
    {
        openSelectDialog(
            message,
            new List<string> { defaultText },
            new List<string> { defaultText },
            callback,
            null);
    }

    public void openSelectDialog(string message, List<string> options, call callback)
    {
        openSelectDialog(message, options, options, callback, null);
    }

    public void openSelectDialog(
        string message,
        List<string> values,
        List<string> displayOptions,
        call callback,
        call cancelCallback)
    {
        _cancelCallback = cancelCallback;
        isDropdownMode = true;
        optionValues = values ?? new List<string>();
        text.text = message;
        if (addPartsList == null)
        {
            Debug.LogError("[UI_InputBox] 選択ダイアログ用Dropdownが設定されていません。");
            cancelCallback?.Invoke("");
            return;
        }
        addPartsList.ClearOptions();
        addPartsList.AddOptions(displayOptions ?? optionValues);
        addPartsList.value = 0;
        _callBack = callback;
        input.gameObject.SetActive(false);
        addPartsList.gameObject.SetActive(true);
        gameObject.SetActive(true);
    }

    public void openSelectDialog(string message, List<string> options, call callback, call cancelCallback)
    {
        openSelectDialog(message, options, options, callback, cancelCallback);
    }

    public void openNoTextBoxDialog(string message, call callback)
    {
        openNoTextBoxDialog(message, callback, null);
    }

    public void openNoTextBoxDialog(string message, call callback, call cancelCallback)
    {
        _cancelCallback = cancelCallback;
        isDropdownMode = false;
        optionValues = null;
        text.text = message;
        _callBack = callback;
        if (input != null)
            input.gameObject.SetActive(false);
        if (addPartsList != null)
            addPartsList.gameObject.SetActive(false);
        gameObject.SetActive(true);
    }

    public void Ok()
    {
        call callback = _callBack;
        string result = isDropdownMode
            ? optionValues != null && addPartsList.value >= 0 && addPartsList.value < optionValues.Count
                ? optionValues[addPartsList.value]
                : addPartsList.options[addPartsList.value].text
            : input.text;

        _cancelCallback = null;
        gameObject.SetActive(false);
        callback?.Invoke(result);
    }

    public void cancel()
    {
        call cancelCallback = _cancelCallback;
        _cancelCallback = null;
        optionValues = null;
        gameObject.SetActive(false);
        cancelCallback?.Invoke("");
    }
}
