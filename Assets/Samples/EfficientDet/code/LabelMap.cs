using UnityEngine;
using System;
using System.Linq;

// 這個類別不需要掛載到任何物件上，它只是一個輔助工具
public class LabelMap
{
    private string[] labels;

    // 建構函式，傳入 TextAsset 來初始化
    public LabelMap(TextAsset labelMapAsset)
    {
        if (labelMapAsset != null)
        {
            // 參考您的範例，用換行符來分割標籤文字
            labels = labelMapAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            labels = new string[0];
            Debug.LogError("LabelMap Asset is null!");
        }
    }

    // 根據 ID 取得標籤名稱
    public string GetLabelName(int id)
    {
        if (labels.Length == 0 || id < 0 || id >= labels.Length)
        {
            return "?";
        }
        return labels[id].Trim();
    }
}