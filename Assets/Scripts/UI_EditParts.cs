using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RuntimeHandle;

public class UI_EditParts : MonoBehaviour
{
    public RoboStructure robo;
    public RoboStructure prevRobo;
    public UI_EditAni ea;

    [Header("Parts List")]
    public int index = 0;
    public Text selectedPartsText;
    List<string> list = new List<string>();
    List<GameObject> items = new List<GameObject>();
    List<bool> selected = new List<bool>();
    public GameObject Template;
    public Color selectedColor;
    public Color deselectedColor;

    [Header("Transform Editing")]
    public InputField PosX;
    public InputField PosY;
    public InputField PosZ;
    public InputField RotX;
    public InputField RotY;
    public InputField RotZ;
    public InputField ScaleX;
    public InputField ScaleY;
    public InputField ScaleZ;
    public RuntimeTransformHandle handle;
    public Space space = Space.Self;
    int moveType = 0;
    Vector3 mousePosition = Vector3.zero;
    public FreeCam fc;
    public bool lockHandle = false;
    public bool disableGOUpdates = false;
    public Toggle syncContraints;

    [Header("Copy/Paste")]
    public hod2v1 cpHod;
    public bool[] cpSelected;
    public int cpIndex;

    [Header("Add/Remove Parts")]
    public GameObject addPartsPanel;
    public Dropdown addPartsList;
    public Text addText;
    public UI_MsgBox msgBox;
    public UI_InputBox inputBox;
    public UI_InputBox kakuninBox;

    void OnEnable()
    {
        if (handle != null)
            handle.isDraggingHandle.AddListener(OnHandleDragging);
    }

    void OnDisable()
    {
        if (handle != null)
            handle.isDraggingHandle.RemoveListener(OnHandleDragging);
    }

    void Update()
    {
    }

    void OnHandleDragging()
    {
        if (disableGOUpdates || robo == null || robo.ani == null || ea == null || ea.cAnim == null ||
            ea.cAnim.frames == null || ea.cAnim.frames.Count == 0)
            return;

        TransformTextUpdate();
        bool sync = syncContraints == null || syncContraints.isOn;
        robo.updatePart(ea.animDD.value, ea.hodDD.value, index, sync);
    }

    public void PopulatePartsList()
    {
        int[] parents, depths;
        string error;
        if (!HodHierarchyRepair.TryGetDisplayHierarchy(robo.hod.parts, out parents, out depths, out error))
            throw new System.IO.InvalidDataException(error);
        clear();
        for (int i = 0; i < robo.parts.Count; i++)
        {
            string offset = "";
            for (int j = 0; j < depths[i]; j++)
                offset += "   ";
            addItem(robo.parts[i], offset + "|_" + robo.parts[i].name);
        }
    }

    public void SelectedIndexChanged(int _index)
    {
        if (handle != null)
            handle.target = robo.parts[_index].transform;
        if (index == _index)
            return;

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            index = _index;
            selected[_index] = true;
        }
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            int[] range = new int[2];
            if (_index > index)
            {
                range[0] = index;
                range[1] = _index;
            }
            else
            {
                range[0] = _index;
                range[1] = index;
            }

            for (int i = 0; i < list.Count; i++)
                selected[i] = i >= range[0] && i <= range[1];

            index = _index;
        }
        else
        {
            index = _index;
            for (int i = 0; i < list.Count; i++)
                selected[i] = _index == i;
        }

        for (int i = 0; i < list.Count; i++)
        {
            ColorBlock cb = items[i].GetComponent<Button>().colors;
            cb.normalColor = selected[i] ? selectedColor : deselectedColor;
            items[i].GetComponent<Button>().colors = cb;
        }
        TransformTextUpdate();
        UpdateSelectedPartsText();
    }

    private void UpdateSelectedPartsText()
    {
        if (selectedPartsText == null)
            return;

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < selected.Count; i++)
        {
            if (!selected[i])
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(robo.parts[i].name);
        }
        SetTextIfChanged(selectedPartsText, sb.ToString());
    }

    public void clear()
    {
        foreach (var item in items)
            GameObject.Destroy(item);

        list.Clear();
        items.Clear();
        selected.Clear();
    }

    public void addItem(GameObject prt, string item)
    {
        list.Add(item);
        GameObject GO = GameObject.Instantiate(Template, Template.transform.parent);
        items.Add(GO);
        selected.Add(false);
        Button b = GO.GetComponent<Button>();
        int lPos = list.Count - 1;
        b.onClick.AddListener(() => { SelectedIndexChanged(lPos); });
        Toggle tg = GO.GetComponentInChildren<Toggle>();
        if (tg != null)
            tg.onValueChanged.AddListener((bool value) => { if (prt.GetComponent<MeshRenderer>() != null) prt.GetComponent<MeshRenderer>().enabled = value; });

        Text t = GO.transform.GetChild(0).GetComponent<Text>();
        t.text = item;
        GO.SetActive(true);
    }

    public void PositionCursor()
    {
        handle.type = HandleType.POSITION;
    }

    public void RotationCursor()
    {
        handle.type = HandleType.ROTATION;
    }

    public void HundleChange(int s)
    {
        if (s == 0)
            handle.type = HandleType.POSITION;
        if (s == 1)
            handle.type = HandleType.ROTATION;
        if (s == 2)
            handle.type = HandleType.SCALE;
    }

    public void spaceChange(int s)
    {
        if (s == 0)
            handle.space = HandleSpace.WORLD;
        else
            handle.space = HandleSpace.LOCAL;

        space = (Space)s;
        TransformTextUpdate();
    }

    public void TransformTextUpdate()
    {
        if (robo == null || robo.parts == null || robo.parts.Count == 0)
            return;
        if (index < 0 || index >= robo.parts.Count)
            index = 0;

        Transform tr = robo.parts[index].transform;
        Vector3 position;
        Vector3 euler;
        if (space == Space.Self)
        {
            position = tr.localPosition;
            euler = tr.localRotation.eulerAngles;
        }
        else
        {
            position = tr.position;
            euler = tr.rotation.eulerAngles;
        }

        SetTextIfChanged(PosX, position.x.ToString());
        SetTextIfChanged(PosY, position.y.ToString());
        SetTextIfChanged(PosZ, position.z.ToString());
        SetTextIfChanged(RotX, euler.x.ToString());
        SetTextIfChanged(RotY, euler.y.ToString());
        SetTextIfChanged(RotZ, euler.z.ToString());
        SetTextIfChanged(ScaleX, tr.localScale.x.ToString());
        SetTextIfChanged(ScaleY, tr.localScale.y.ToString());
        SetTextIfChanged(ScaleZ, tr.localScale.z.ToString());

        if (robo.ani != null && ea != null && ea.cAnim != null)
            ea.setConstraintText();
    }

    static void SetTextIfChanged(Text text, string value)
    {
        if (text != null && text.text != value)
            text.text = value;
    }

    static void SetTextIfChanged(InputField field, string value)
    {
        if (field != null && field.text != value)
            field.text = value;
    }

    public void TransformValueUpdate()
    {
        if (!disableGOUpdates)
        {
            hod2v1_Part prt = new hod2v1_Part();
            Vector3 position = new Vector3();
            if (float.TryParse(PosX.text, out position.x) &&
                float.TryParse(PosY.text, out position.y) &&
                float.TryParse(PosZ.text, out position.z))
            {
                prt.position = position;
            }

            Vector3 euler = new Vector3();
            if (float.TryParse(RotX.text, out euler.x) &&
                float.TryParse(RotY.text, out euler.y) &&
                float.TryParse(RotZ.text, out euler.z))
            {
                prt.rotation = Quaternion.Euler(euler);
            }

            Vector3 scale = new Vector3();
            if (float.TryParse(ScaleX.text, out scale.x) &&
                float.TryParse(ScaleY.text, out scale.y) &&
                float.TryParse(ScaleZ.text, out scale.z))
                prt.scale = scale;
            if (robo.ani != null)
                robo.updatePart(ea.animDD.value, ea.hodDD.value, index, prt, space);
            else
                robo.updatePart(index, prt, space);
        }
    }

    public void copyValues()
    {
        cpHod = robo.createHod2v1();
        cpIndex = index;
        cpSelected = selected.ToArray();
    }

    public void pasteValues()
    {
        if (cpHod == null || cpHod.parts == null || cpSelected == null)
            return;

        int count = selected.FindAll(x => x == true).Count;
        if (count > 1)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (i < cpSelected.Length && i < cpHod.parts.Count && cpSelected[i])
                    robo.updatePart(i, cpHod.parts[i]);
            }
        }
        else if (cpIndex >= 0 && cpIndex < cpHod.parts.Count)
        {
            robo.updatePart(index, cpHod.parts[cpIndex]);
        }

        TransformTextUpdate();
    }

    public void addParts()
    {
        BeginStructureEdit(OpenAddPartsPanel);
    }

    void OpenAddPartsPanel()
    {
        addPartsPanel.SetActive(true);
        addText.text = UILocalization.Get(UILocalizationKeys.AddPartUnder, "{0}の下に新規パーツを追加します。", robo.hod.parts[index].name);
        DirectoryInfo di = new DirectoryInfo(robo.folder);
        FileInfo[] files = di.GetFiles();
        List<string> fName = new List<string>();
        for (int i = 0; i < files.Length; i++)
        {
            if (files[i].Extension == ".x")
                fName.Add(files[i].Name);
        }
        addPartsList.ClearOptions();
        addPartsList.AddOptions(fName);
    }

    public void addParts2()
    {
        string warning;
        if (!robo.CanEditStructure(out warning))
        {
            msgBox.Show(warning);
            addPartsPanel.SetActive(false);
            return;
        }
        string error;
        if (!robo.TryAddPart(addPartsList.options[addPartsList.value].text, index, out error))
        {
            msgBox.Show(error);
            addPartsPanel.SetActive(false);
            return;
        }
        RebuildPrevRoboIfNeeded();
        addPartsPanel.SetActive(false);
        PopulatePartsList();
        if (robo.ani != null && ea != null)
            robo.setPose(ea.animDD.value, ea.hodDD.value);
    }

    public void addPartsCancel()
    {
        addPartsPanel.SetActive(false);
    }

    public void removePart()
    {
        if (robo == null || robo.hod == null || robo.hod.parts == null || index < 0 || index >= robo.hod.parts.Count)
        {
            msgBox.Show(UILocalization.Get(UILocalizationKeys.SelectPartToDelete, "削除するパーツを選択してください。"));
            return;
        }

        BeginStructureEdit(ContinueRemovePart);
    }

    void ContinueRemovePart()
    {
        if (index == 0)
        {
            msgBox.Show(UILocalization.Get(UILocalizationKeys.RootPartCannotDelete, "ルートパーツは削除できません。"));
            return;
        }

        int partIndexToDelete = index;
        int selectionAfterDelete = FindParentIndex(partIndexToDelete);
        string confirmationMessage = robo.hod.parts[partIndexToDelete].childCount > 0
            ? UILocalization.Get(UILocalizationKeys.DeletePartWithChildren, "子パーツごと削除しますがよろしいですか？")
            : UILocalization.Get(UILocalizationKeys.DeleteSelectedPart, "選択パーツを削除します。");

        kakuninBox.openNoTextBoxDialog(confirmationMessage, (string rText) =>
        {
            string error;
            if (!robo.TryRemovePart(partIndexToDelete, out error))
            {
                msgBox.Show(string.IsNullOrEmpty(error)
                    ? UILocalization.Get(UILocalizationKeys.PartDataInvalidDelete, "パーツ情報の整合性を確認できないため削除できません。")
                    : error);
            }
            else
            {
                RebuildPrevRoboIfNeeded();
                PopulatePartsList();
                if (robo.ani != null && ea != null)
                    robo.setPose(ea.animDD.value, ea.hodDD.value);

                if (robo.parts.Count > 0)
                {
                    int nextIndex = Mathf.Clamp(selectionAfterDelete, 0, robo.parts.Count - 1);
                    index = -1;
                    SelectedIndexChanged(nextIndex);
                }
            }
        });
    }

    void BeginStructureEdit(System.Action continuation)
    {
        string warning;
        if (robo.CanEditStructure(out warning))
        {
            continuation?.Invoke();
            return;
        }

        LegacyAniStructureEditPlan plan;
        string error;
        if (!robo.TryCreateLegacyStructureEditPlan(out plan, out error))
        {
            msgBox.Show(string.IsNullOrEmpty(error) ? warning : error);
            return;
        }

        if (plan.RequiresAuthorityChoice)
        {
            List<string> values = new List<string> { "treeDepth", "childCount" };
            List<string> displayOptions = new List<string>
            {
                UILocalization.GetFixed("treeDepthを正として修復"),
                UILocalization.GetFixed("childCountを正として修復")
            };
            string message = BuildLegacyStructureEditMessage(plan, true);
            inputBox.openSelectDialog(
                message,
                values,
                displayOptions,
                selected => ApplyLegacyStructureEditPlan(
                    plan,
                    selected == "childCount"
                        ? LegacyAniHierarchyAuthority.ChildCount
                        : LegacyAniHierarchyAuthority.TreeDepth,
                    continuation),
                null);
            return;
        }

        LegacyAniHierarchyAuthority authority = plan.RecommendedAuthority;
        string confirmation = BuildLegacyStructureEditMessage(plan, false);
        kakuninBox.openNoTextBoxDialog(
            confirmation,
            ignored => ApplyLegacyStructureEditPlan(plan, authority, continuation));
    }

    string BuildLegacyStructureEditMessage(
        LegacyAniStructureEditPlan plan,
        bool requiresChoice)
    {
        string prefix = requiresChoice
            ? UILocalization.Get(
                "hod.legacy_edit.choose_authority",
                "旧ANIのtreeDepthとchildCountが別の有効な階層を表しています。パーツの追加・削除を行うには、構造HODと全フレームへ適用する正本を選択してください。元ファイルは保存するまで変更されません。")
            : UILocalization.Get(
                "hod.legacy_edit.confirm_normalize",
                "旧ANIは読込時の階層値を維持しています。パーツの追加・削除を行うには、構造HODと全フレームの階層列を次の内容で正規化します。元ファイルは保存するまで変更されません。正規化して構造編集を続けますか？");

        string details = plan.Summary;
        if (!string.IsNullOrEmpty(plan.Details))
            details += "\n" + plan.Details;

        if (requiresChoice)
        {
            string treePreview = plan.BuildPreview(
                LegacyAniHierarchyAuthority.TreeDepth, robo.hod.parts);
            string childPreview = plan.BuildPreview(
                LegacyAniHierarchyAuthority.ChildCount, robo.hod.parts);
            details += UILocalization.Get(
                "hod.legacy_edit.choice_previews",
                "\n\n[treeDepth]\n{0}\n\n[childCount]\n{1}",
                treePreview,
                childPreview);
        }
        else
        {
            string preview = plan.BuildPreview(plan.RecommendedAuthority, robo.hod.parts);
            if (!string.IsNullOrEmpty(preview))
                details += "\n" + preview;
        }

        return prefix + "\n\n" + details;
    }

    void ApplyLegacyStructureEditPlan(
        LegacyAniStructureEditPlan plan,
        LegacyAniHierarchyAuthority authority,
        System.Action continuation)
    {
        string error;
        if (!robo.TryApplyLegacyStructureEditPlan(plan, authority, out error))
        {
            msgBox.Show(error);
            return;
        }

        RebuildPrevRoboIfNeeded();
        PopulatePartsList();
        continuation?.Invoke();
    }

    public void renamePart()
    {
        inputBox.openDialog(UILocalization.Get(UILocalizationKeys.PartSelectRename, "どのパーツの名前を変更しますか？"), robo.hod.parts[index].name, (string rText) =>
        {
            if (!File.Exists(Path.Combine(robo.folder, rText)))
                msgBox.Show(UILocalization.Get(UILocalizationKeys.PartMissingUseEmpty, "指定名のパーツはフォルダに存在しません。空のパーツと入れ替えます。"));

            robo.renamePart(index, rText);
            RebuildPrevRoboIfNeeded();
            PopulatePartsList();
            if (robo.ani != null && ea != null)
                robo.setPose(ea.animDD.value, ea.hodDD.value);
        });
    }

    void RebuildPrevRoboIfNeeded()
    {
        if (robo == null || robo.ani == null || prevRobo == null)
            return;

        prevRobo.buildStructureFromLoaded(robo, robo.ani.structure);
        foreach (GameObject prt in prevRobo.parts)
            prt.layer = 7;
    }


    int FindParentIndex(int partIndex)
    {
        int parentDepth = robo.hod.parts[partIndex].treeDepth - 1;
        for (int i = partIndex - 1; i >= 0; i--)
        {
            if (robo.hod.parts[i].treeDepth == parentDepth)
                return i;
        }
        return 0;
    }
}
