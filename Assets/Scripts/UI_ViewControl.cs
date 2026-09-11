using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class UI_ViewControl : MonoBehaviour
{
    public GameObject Menu;
    
    [Header("EditPanel")]
    public GameObject EditPanel;
    public GameObject EditMenu;
    public GameObject ModeSelectDD;
    public GameObject Handle3D;
    public GameObject SelectPanel;
    public RoboStructure robo;
    public UI_EditAni ea;
    

    [Header("AnimPreview")]
    public GameObject PrevPanel;
    public GameObject PrevWindow;

    [Header("Settings")]
    public InputField folderLoc;
    
    // Start is called before the first frame update
    void Start()
    {
        Menu.SetActive(false);
        EditMode(false);
        loadSettings();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ModeSelect(int id)
    {
        TestPlayController testPlay = GetComponent<TestPlayController>();
        if (testPlay != null)
        {
            if (id == 2)
                testPlay.StartTestPlay();
            else if (testPlay.playModeActive)
                testPlay.StopTestPlay();
        }

        switch (id)
        {
            case 0:
                EditMode(true);
                if (PrevPanel != null)
                    PreviewMode(false);
                SelectMode(false);
                Handle3D.SetActive(true);
                ModeSelectDD.SetActive(true);
                break;
            case 1:
                EditMode(false);
                if (PrevPanel != null)
                    PreviewMode(true);
                SelectMode(false);
                Handle3D.SetActive(false);
                ModeSelectDD.SetActive(true);
                break;
            case 2:
                EditMode(false);
                if (PrevPanel != null)
                    PreviewMode(false);
                SelectMode(false);
                Handle3D.SetActive(false);
                ModeSelectDD.SetActive(true);
                break;
            case 3:
                EditMode(false);
                if (PrevPanel != null)
                    PreviewMode(false);
                Handle3D.SetActive(false);
                ModeSelectDD.SetActive(false);                
                SelectMode(true);
                break;
        }
    }
    public void EditMode(bool enabled)
    {
        EditPanel.SetActive(enabled);
        EditMenu.SetActive(enabled);
        if (robo.ani != null)
            PrevWindow.SetActive(enabled);
        if (enabled)
        {
            if (robo.ani != null)
            { 
                
                ea.selectHOD();
            }
        }
    }

    public void PreviewMode(bool enabled)
    {
        PrevPanel.SetActive(enabled);
        if (enabled)
        {
            PrevPanel.GetComponent<AniPreview>().updateList();
            PrevPanel.GetComponent<AniPreview>().setAnimation(ea.animDD.value);
        }
        else
            PrevPanel.GetComponent<AniPreview>().animator.play = false;
    }

    public void SelectMode(bool enabled)
    {
        SelectPanel.SetActive(enabled);
    }


    public void saveSettings()
    {
        string folder = SelectPanel.GetComponent<UI_SelectMech>().folder;
        WindomToolSettings.SaveFolder(folder);
    }

    public void loadSettings()
    {
        if (WindomToolSettings.Exists)
        {
            WindomToolSettings.Data settings = WindomToolSettings.Load();
            folderLoc.text = settings.folder;
        }
    }
}
