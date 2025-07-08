using System;
using UnityEngine;

namespace TensorFlowLite
{
    /// <summary>
    /// EfficientDet
    /// Object Detection
    /// </summary>
    public sealed class EfficientDet : BaseVisionTask
    {
        [Serializable]
        public class Options
        {
            [FilePopup("*.tflite")]
            public string modelPath = string.Empty;
            public AspectMode aspectMode = AspectMode.Fit;
            public TfLiteDelegateType delegateType = TfLiteDelegateType.GPU;
        }

        public readonly struct Result
        {
            public readonly int classID;
            public readonly float score;
            public readonly Rect rect;

            public Result(int classID, float score, Rect rect)
            {
                this.classID = classID;
                this.score = score;
                this.rect = rect;
            }
        }

        public int inputWidth { get; private set; }
        public int inputHeight { get; private set; }

        const int MAX_DETECTION = 25;

        // [修改] 為了清晰起見，重新命名輸出陣列
        private readonly float[] output_locations = new float[MAX_DETECTION * 4]; // Bounding boxes
        private readonly float[] output_classes = new float[MAX_DETECTION];       // Classes
        private readonly float[] output_scores = new float[MAX_DETECTION];        // Scores
        // [新增] 標準模型通常還有第四個輸出：偵測到的物件總數
        private readonly float[] num_detections = new float[1];

        private readonly Result[] results = new Result[MAX_DETECTION];

        public EfficientDet(Options options)
        {
            var interpreterOptions = new InterpreterOptions();
            interpreterOptions.AutoAddDelegate(options.delegateType, typeof(byte));
            Load(FileUtil.LoadFile(options.modelPath), interpreterOptions);

            var inputShape = interpreter.GetInputTensorInfo(0).shape;
            inputHeight = inputShape[1];
            inputWidth = inputShape[2];
        }

        public EfficientDet(Options options, InterpreterOptions interpreterOptions)
        {
            Load(FileUtil.LoadFile(options.modelPath), interpreterOptions);

            var inputShape = interpreter.GetInputTensorInfo(0).shape;
            inputHeight = inputShape[1];
            inputWidth = inputShape[2];
        }

        // [修改] 這是最關鍵的修改。我們將 PostProcess 改為最常見、最標準的輸出順序。
        // 大多數 TensorFlow Hub 上的物件偵測模型都遵循此順序。
        protected override void PostProcess()
        {
            // 索引 0: Bounding Box Locations (y_min, x_min, y_max, x_max)
            interpreter.GetOutputTensorData(0, output_locations.AsSpan());
            // 索引 1: Classes
            interpreter.GetOutputTensorData(1, output_classes.AsSpan());
            // 索引 2: Scores
            interpreter.GetOutputTensorData(2, output_scores.AsSpan());
            // 索引 3: Number of detections
            interpreter.GetOutputTensorData(3, num_detections.AsSpan());
        }

        public ReadOnlySpan<Result> GetResults()
        {
            for (int i = 0; i < MAX_DETECTION; i++)
            {
                // [修改] TensorFlow Hub 的標準 EfficientDet-Lite 模型輸出座標順序是 [ymin, xmin, ymax, xmax]
                // 這裡我們根據標準順序來解析
                int current = i * 4;
                float ymin = output_locations[current];
                float xmin = output_locations[current + 1];
                float ymax = output_locations[current + 2];
                float xmax = output_locations[current + 3];

                // 轉換座標以適應 Unity UI 空間 (Y軸是反的)
                // top 在 Unity 中是 1 - ymin
                float top = 1f - ymin;
                // bottom 在 Unity 中是 1 - ymax
                float bottom = 1f - ymax;
                float left = xmin;
                float right = xmax;

                results[i] = new Result(
                    classID: (int)output_classes[i],
                    score: output_scores[i],
                    rect: new Rect(left, top, right - left, top - bottom));
            }
            return results;
        }
    }
}