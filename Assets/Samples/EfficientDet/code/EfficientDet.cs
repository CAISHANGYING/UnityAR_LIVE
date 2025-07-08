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

        // [�ק�] ���F�M���_���A���s�R�W��X�}�C
        private readonly float[] output_locations = new float[MAX_DETECTION * 4]; // Bounding boxes
        private readonly float[] output_classes = new float[MAX_DETECTION];       // Classes
        private readonly float[] output_scores = new float[MAX_DETECTION];        // Scores
        // [�s�W] �зǼҫ��q�`�٦��ĥ|�ӿ�X�G�����쪺�����`��
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

        // [�ק�] �o�O�����䪺�ק�C�ڭ̱N PostProcess �אּ�̱`���B�̼зǪ���X���ǡC
        // �j�h�� TensorFlow Hub �W�����󰻴��ҫ�����`�����ǡC
        protected override void PostProcess()
        {
            // ���� 0: Bounding Box Locations (y_min, x_min, y_max, x_max)
            interpreter.GetOutputTensorData(0, output_locations.AsSpan());
            // ���� 1: Classes
            interpreter.GetOutputTensorData(1, output_classes.AsSpan());
            // ���� 2: Scores
            interpreter.GetOutputTensorData(2, output_scores.AsSpan());
            // ���� 3: Number of detections
            interpreter.GetOutputTensorData(3, num_detections.AsSpan());
        }

        public ReadOnlySpan<Result> GetResults()
        {
            for (int i = 0; i < MAX_DETECTION; i++)
            {
                // [�ק�] TensorFlow Hub ���з� EfficientDet-Lite �ҫ���X�y�ж��ǬO [ymin, xmin, ymax, xmax]
                // �o�̧ڭ̮ھڼзǶ��ǨӸѪR
                int current = i * 4;
                float ymin = output_locations[current];
                float xmin = output_locations[current + 1];
                float ymax = output_locations[current + 2];
                float xmax = output_locations[current + 3];

                // �ഫ�y�ХH�A�� Unity UI �Ŷ� (Y�b�O�Ϫ�)
                // top �b Unity ���O 1 - ymin
                float top = 1f - ymin;
                // bottom �b Unity ���O 1 - ymax
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