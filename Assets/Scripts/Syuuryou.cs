using UnityEngine;

public class Syuuryou: MonoBehaviour
{
   //ゲーム終了:ボタンから呼び出す
    public void EndGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;//ゲームプレイ終了
#else
    Application.Quit();//ゲームプレイ終了
#endif
    }
}