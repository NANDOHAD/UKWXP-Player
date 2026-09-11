using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
public class AniPreview : MonoBehaviour
{
    public RoboStructure robo;
    public MechaAnimator animator;
    
    [Header("Animation List")]
    public int index = 0;
    List<string> list = new List<string>();
    List<GameObject> items = new List<GameObject>();
    public GameObject Template;
    public Color selectedColor;
    public Color deselectedColor;

    [Header("Settings")]
    public Text txtPlay;
    public string playTxt = "再生";
    public string pauseTxt = "一時停止";
    public Toggle tglLoop;
    public InputField stepDirect;
    // Start is called before the first frame update
    void Start()
    {
        RefreshPlayText();
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        RefreshPlayText();
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
    }

    void OnLocaleChanged(Locale locale)
    {
        RefreshPlayText();
    }

    void RefreshPlayText()
    {
        if (txtPlay == null)
            return;

        string japaneseText = animator != null && animator.play ? pauseTxt : playTxt;
        txtPlay.text = UILocalization.GetFixed(japaneseText);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void updateList()
    {
        clear();
        int count = robo.ani.animations.Count;
        for (int i = 0; i < count; i++)
            addItem(((i).ToString() + " - " + robo.ani.animations[i].name));

    }

    public void SelectedIndexChanged(int _index)
    {

        if (index == _index)
            return;
        else
            index = _index;

        for (int i = 0; i < list.Count; i++)
        {
            ColorBlock cb = items[i].GetComponent<Button>().colors;
            if (i == index)
                cb.normalColor = selectedColor;
            else
                cb.normalColor = deselectedColor;
            items[i].GetComponent<Button>().colors = cb;
        }
       
        setAnimation(index);
    }

    public void clear()
    {
        foreach (var item in items)
            GameObject.Destroy(item);

        list.Clear();
        items.Clear();
    }

    public void addItem(string item)
    {
        list.Add(item);
        GameObject GO = GameObject.Instantiate(Template, Template.transform.parent);
        items.Add(GO);
        Button b = GO.GetComponent<Button>();
        int lPos = list.Count - 1;
        b.onClick.AddListener(() => { SelectedIndexChanged(lPos); });
        Text t = GO.transform.GetChild(0).GetComponent<Text>();
        t.text = item;
        GO.SetActive(true);
    }
    public void setAnimation(int id)
    {

        if (robo.ani.animations[id].frames.Count > 0 && robo.ani.animations[id].scripts.Count > 0)
        {
            animator.run(robo.ani.animations[id], tglLoop.isOn);
            return;
        }

        animation nAnim = new animation();
        if (id > 49 && id < 100)
        {
            nAnim.frames = robo.ani.animations[id].frames;
            nAnim.scripts = robo.ani.animations[id - 50].scripts;
            if (nAnim.frames.Count > 0 && nAnim.scripts.Count > 0)
                animator.run(nAnim, tglLoop.isOn);
        }

        if (id == 101 || id == 102)
        {
            nAnim.frames = robo.ani.animations[id].frames;
            nAnim.scripts = robo.ani.animations[100].scripts;
            if (nAnim.frames.Count > 0 && nAnim.scripts.Count > 0)
                animator.run(nAnim, tglLoop.isOn);
        }

    }

    public void playPause()
    {
        if (animator.play)
        {
            animator.play = false;
            RefreshPlayText();
        }
        else
        {
            animator.play = true;
            RefreshPlayText();
        }
    }

    public void stepPlay()
    {
        float a = 0;
        Vector3 Direction = robo.root.transform.forward;
        if (float.TryParse(stepDirect.text, out a))
            Direction = Quaternion.AngleAxis(a, Vector3.up) * robo.root.transform.forward;

            
        float angle = Vector3.SignedAngle(transform.forward, Direction, Vector3.up);
        //Step.registeredAnim = new int[] { 9, 10, 11, 12 };
        if (Mathf.Abs(angle) > 90)
        {
            angle = Vector3.SignedAngle(-transform.forward, Direction, Vector3.up);
            if (angle >= 0)
            {
                //back to left
                
                animator.run(new animation[] { robo.ani.animations[12], robo.ani.animations[9] }, Mathf.Abs(angle) / 90);

            }
            else
            {
                //back to right
                animator.run(new animation[] { robo.ani.animations[12], robo.ani.animations[10] }, Mathf.Abs(angle) / 90);
            }

        }
        else
        {
            if (angle >= 0)
            {
                //forward to right
                animator.run(new animation[] { robo.ani.animations[11], robo.ani.animations[10] }, Mathf.Abs(angle) / 90);
            }
            else
            {
                //forward to left
                animator.run(new animation[] { robo.ani.animations[11], robo.ani.animations[9] }, Mathf.Abs(angle) / 90);

            }
        }
    }




    public void stepEXArts1()
    {
        animator.run(new animation[] {robo.ani.animations[104]},0);
    }

    public void stepEXArts2()
    {
        animator.run(new animation[] {robo.ani.animations[105]},0);
    }

    public void stepEXArts3()
    {
        animator.run(new animation[] {robo.ani.animations[109]},0);
    }


    public void tglChange()
    {
        setAnimation(index);
    }
}
