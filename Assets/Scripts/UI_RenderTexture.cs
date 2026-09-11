using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_RenderTexture : MonoBehaviour
{
    public RenderTexture m_Texture;
    RectTransform rectTransform;
    int lastWidth = -1;
    int lastHeight = -1;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void OnEnable()
    {
        ApplyTextureSize(force: true);
    }

    void OnRectTransformDimensionsChange()
    {
        ApplyTextureSize(force: false);
    }

    void ApplyTextureSize(bool force)
    {
        if (m_Texture == null)
            return;

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
            return;

        Vector2 size = rectTransform.sizeDelta;
        int width = Mathf.Max(1, Mathf.RoundToInt(size.x * 1920f));
        int height = Mathf.Max(1, Mathf.RoundToInt(size.y * 1080f));

        if (!force && width == lastWidth && height == lastHeight)
            return;

        lastWidth = width;
        lastHeight = height;

        if (m_Texture.width == width && m_Texture.height == height)
            return;

        m_Texture.Release();
        m_Texture.width = width;
        m_Texture.height = height;
    }
}
