using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
public class MenuBar : MonoBehaviour
{
    public List<GameObject> MenuPanels;
    public EventSystem es;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (!es.IsPointerOverGameObject())
        {
            for (int i = 0; i < MenuPanels.Count; i++)
            {
                MenuPanels[i].SetActive(false);
            }
        }
    }

    public void openPanel(int index)
    {
        for (int i = 0; i < MenuPanels.Count; i++)
        {
            if (i == index)
                MenuPanels[i].SetActive(true);
            else
                MenuPanels[i].SetActive(false);
        }
    }
}
