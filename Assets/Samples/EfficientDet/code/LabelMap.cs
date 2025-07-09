using UnityEngine;
using System;
using System.Linq;

// �o�����O���ݭn��������󪫥�W�A���u�O�@�ӻ��U�u��
public class LabelMap
{
    private string[] labels;

    // �غc�禡�A�ǤJ TextAsset �Ӫ�l��
    public LabelMap(TextAsset labelMapAsset)
    {
        if (labelMapAsset != null)
        {
            // �Ѧұz���d�ҡA�δ���ŨӤ��μ��Ҥ�r
            labels = labelMapAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            labels = new string[0];
            Debug.LogError("LabelMap Asset is null!");
        }
    }

    // �ھ� ID ���o���ҦW��
    public string GetLabelName(int id)
    {
        if (labels.Length == 0 || id < 0 || id >= labels.Length)
        {
            return "?";
        }
        return labels[id].Trim();
    }
}