using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum scriptVarType {EMPTY,STR, NUM }
public struct scriptVar
{
    public scriptVarType type;
    public string str;
    public float num;
}

public delegate void scriptFunc(scriptVar[] values);
public delegate void scriptSetVar(scriptVar value);
public delegate scriptVar scriptGetVar();


public class scriptInterpreter
{
    string[] lines;
    int lineLoc = 0;
    int invalidLine = 0;
    List<string> invalidLines = new List<string> ();
    public Dictionary<string, scriptFunc> registeredFunc = new Dictionary<string, scriptFunc>();
    public Dictionary<string, scriptSetVar> registeredSetVar = new Dictionary<string, scriptSetVar>();
    public Dictionary<string, scriptGetVar> registeredGetVar = new Dictionary<string,scriptGetVar>();
    public Dictionary<string, scriptVar> registeredStaticVar = new Dictionary<string, scriptVar> ();
    
    public void runScript(string script)
    {
        invalidLine = 0;
        lines = script.Split((char)0x0A);
        lineLoc = 0;
        for (; lineLoc < lines.Length; lineLoc++)
        {
            string line = lines[lineLoc].Trim();
            InterpretLine(line);
        }
    }

    public void InterpretLine(string line)
    { 
        Debug.Log(line);
        string[] s = line.Split(';');
        if (line[0] == '\'')
            return;

        if (line.Contains("(") && line.Contains(")"))
        {
            int pLeftLoc = line.IndexOf("(");
            int pRightLoc = line.IndexOf(")");
            if (pLeftLoc < pRightLoc)
            {
                
                s = s[0].Split("(".ToCharArray());
                string fName = s[0].Trim();

                s = s[1].Split(")".ToCharArray());
                s = s[0].Split(",".ToCharArray());
                if (fName.Trim() == "IF")
                {
                    interpretIFStatement(s);
                }
                else
                {
                    scriptVar[] v = new scriptVar[s.Length];
                    for (int i = 0; i < s.Length; i++)
                    {
                        v[i] = interpretVariable(s[i]);

                    }

                    registeredFunc[fName](v);
                }
            }
        }
        else if (line.Contains("="))
        {
            s = s[0].Split('=');
            registeredSetVar[s[0].Trim()](interpretVariable(s[1]));
        }
    }
    
    public void interpretIFStatement(string[] s)
    {
        scriptVar[] v = new scriptVar[2];
        v[0] = interpretVariable(s[0]);
        v[1] = interpretVariable(s[2]);
        bool conditional = false;
        if (v[0].type == scriptVarType.NUM && v[1].type == scriptVarType.NUM)
        {
            switch(s[1].Trim())
            {
                case "==":
                    conditional = v[0].num == v[1].num;
                    break;
                case ">=":
                    conditional = v[0].num >= v[1].num;
                    break;
                case "<=":
                    conditional = v[0].num <= v[1].num;
                    break;
            }
        }
        lineLoc++;
        for (; lineLoc < lines.Length; lineLoc++)
        {
            string line = lines[lineLoc].Trim();
            if (line.Contains("ELSE;"))
                conditional = !conditional;

            if (line.Contains("ENDIF;"))
                return;
            
            if (conditional)
                InterpretLine(line);
        }
    }

    public void registerFunction(string name, scriptFunc f)
    {
        registeredFunc.Add(name, f);
    }

    public void registerVariable(string name, scriptSetVar sv, scriptGetVar gv)
    {
        registeredSetVar.Add(name, sv);
        registeredGetVar.Add(name, gv);
    } 

    public void registerStaticVariable(string name, scriptVar value)
    {
        if (registeredStaticVar.ContainsKey(name))
            registeredStaticVar[name] = value;
        else
            registeredStaticVar.Add(name, value);
    }

    public scriptVar interpretVariable(string text)
    {
        scriptVar v = new scriptVar();
        v.num = 0;
        v.str = "";
        float f;
        if (float.TryParse((text).ToString(), out f))
        {
            v.type = scriptVarType.NUM;
            v.num = f;
        }
        else if (registeredGetVar.ContainsKey(text.Trim()))
        {
            v = registeredGetVar[text.Trim()]();
        }
        else if (registeredStaticVar.ContainsKey(text.Trim()))
        {
            v = registeredStaticVar[text.Trim()];
        }
        else
        {
            v.type = scriptVarType.STR;
            v.str = text;
        }

        return v;
    }

    public scriptVar convertFloat(float f)
    {
        scriptVar v = new scriptVar();
        v.type = scriptVarType.NUM;
        v.num = f;
        v.str = "";
        return v;
    }

    public scriptVar convertString(string s)
    {
        scriptVar v = new scriptVar();
        v.type = scriptVarType.STR;
        v.str = s;
        v.num = 0;
        return v;
    }

    
}
