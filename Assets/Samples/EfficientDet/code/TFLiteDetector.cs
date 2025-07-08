using UnityEngine;
using TensorFlowLite;
using System.Collections.Generic;
using System;
using System.Linq;

public struct DetectionResult
{
    public Rect Bbox;
    public float Score;
    public int ClassIndex;
    public string Label;
}

public class TFLiteDetector
{
    private EfficientDet efficientDet;
    private string[] labels;
    private float scoreThreshold;

    // �{�b�o�ӥi�H���T�a�q�ק�᪺ EfficientDet ��Ū����ؤo�F
    public int InputWidth => efficientDet.inputWidth;
    public int InputHeight => efficientDet.inputHeight;

    public TFLiteDetector(EfficientDet.Options options, TextAsset labelMapAsset, float threshold)
    {
        efficientDet = new EfficientDet(options);
        labels = labelMapAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        scoreThreshold = threshold;
    }

    public List<DetectionResult> Detect(Texture inputTexture)
    {
        efficientDet.Run(inputTexture);

        var results = efficientDet.GetResults();

        var finalDetections = new List<DetectionResult>();
        foreach (var result in results)
        {
            if (result.score >= scoreThreshold)
            {
                finalDetections.Add(new DetectionResult
                {
                    Bbox = result.rect,
                    Score = result.score,
                    ClassIndex = result.classID,
                    Label = GetLabelName(result.classID)
                });
            }
        }
        return finalDetections;
    }

    private string GetLabelName(int id)
    {
        if (id < 0 || id >= labels.Length)
        {
            return "?";
        }
        return labels[id].Trim();
    }

    public void Dispose()
    {
        efficientDet?.Dispose();
    }
}