using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_MsgBox : MonoBehaviour
{
    public Text text;
    public GameObject masukuObject;

    public void Start()
    {
        if (masukuObject == null)
            Debug.LogError("Masukuオブジェクトがインスペクターで設定されていません。");
        else
            SetMaskActive(gameObject.activeSelf);
    }

    private void OnEnable()
    {
        SetMaskActive(true);
    }

    private void OnDisable()
    {
        SetMaskActive(false);
    }

    public void Show(string message)
    {
        text.text = message;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        else
            SetMaskActive(true);
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
        else
            SetMaskActive(false);
    }

    void SetMaskActive(bool active)
    {
        if (masukuObject != null && masukuObject.activeSelf != active)
            masukuObject.SetActive(active);
    }
}
