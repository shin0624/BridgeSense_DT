using System.Collections.Generic;
using UnityEngine;

namespace BridgeSenseDT.Assessment
{
    /// <summary>
    /// AI 추론 결과(BridgeAnalysisResult)를 안전등급 평가용 DetectedDefect 목록으로 환산하는 어댑터.
    /// SafetyGradeEvaluator가 Sentis/모델 타입을 직접 알 필요가 없도록 이 클래스가 경계를 담당한다.
    ///
    /// 두 모델의 결과를 합치는 방식:
    ///   - RT-DETR: 결함 클래스별 최고 score를 confidence로 사용 (진짜 검출 신뢰도)
    ///   - SegFormer: 결함 클래스별 픽셀 점유율을 maskAreaRatio로 사용 (심각도 크기)
    ///   - 한쪽에서만 잡힌 결함도 버리지 않고 합집합으로 처리한다.
    /// </summary>
    public static class DefectExtractor
    {
        private const int NumDefectClasses = 9;  // 결함 9종 (배경 제외)
        private const int NumSegClasses = 10;    // SegFormer는 배경(0) + 결함 9종

        /// <summary>
        /// SegFormer만 잡은 결함에 부여할 confidence 상한.
        /// SegFormer는 픽셀별 argmax만 남기고 확률값을 버리기 때문에 진짜 신뢰도를 알 수 없다.
        /// 다만 실측 검증(Assets/06.AI/TestImages 60장)에서 SegFormer는 정상 이미지에
        /// false positive가 0%였고 mean_iou도 0.47로 RT-DETR(mAP 0.05)보다 훨씬 신뢰도가 높았다.
        /// 그래서 "면적을 유의미하게 차지했다 = 그만큼 확신이 있다"로 보고 면적률에서 역산하되,
        /// 실제 확률이 아니므로 RT-DETR 검출만큼 신뢰하지는 않도록 상한을 걸어둔다.
        /// ※ 더 정확히 하려면 SegformerModel이 argmax와 함께 클래스별 평균 확률도 반환하도록
        ///    확장한 뒤 이 근사치를 그 값으로 교체할 것.
        /// </summary>
        private const float SegmentationOnlyMaxConfidence = 0.6f;

        /// <summary>이미지 1장의 분석 결과를 결함 목록으로 환산한다.</summary>
        public static List<DetectedDefect> Extract(BridgeAnalysisResult analysis)
        {
            var defects = new List<DetectedDefect>();
            if (analysis == null) return defects;

            float[] maxScorePerClass = GetMaxScorePerClass(analysis.Detections);
            float[] areaRatioPerClass = GetAreaRatioPerClass(analysis.Segmentation);

            // 마스크는 저장하지 않으므로, 결함 위치를 나중에도 보여줄 수 있도록
            // 지금 사각형으로 뽑아 결함과 함께 남긴다.
            List<DefectBox>[] boxesPerClass = MaskBoxExtractor.ExtractAll(analysis.Segmentation);

            for (int classId = 0; classId < NumDefectClasses; classId++)
            {
                float rtdetrScore = maxScorePerClass[classId];
                float areaRatio = areaRatioPerClass[classId];

                bool detectedByRtdetr = rtdetrScore > 0f;
                bool detectedBySegformer = areaRatio > 0f;

                if (!detectedByRtdetr && !detectedBySegformer)
                    continue; // 두 모델 다 이 클래스를 못 봤으면 결함 아님

                var type = (DefectType)classId; // DefectType은 RT-DETR 클래스 id와 값이 일치하도록 선언돼 있음

                // RT-DETR이 잡았으면 그 score를 쓰고, SegFormer만 잡았으면 면적률에서 근사
                float confidence = detectedByRtdetr
                    ? rtdetrScore
                    : EstimateConfidenceFromArea(areaRatio);

                defects.Add(new DetectedDefect
                {
                    type = type,
                    confidence = confidence,
                    maskAreaRatio = areaRatio,
                    estimatedWidthMm = -1f, // 촬영거리·GSD 정보가 없어 균열폭 실측 불가 (model_io_spec.md 참고)
                    isStructurallyCritical = SafetyGradeEvaluator.IsStructurallyCriticalType(type),
                    boxes = boxesPerClass[classId],
                });
            }

            return defects;
        }

        /// <summary>RT-DETR 검출 목록에서 클래스별 최고 score를 뽑는다. 검출 없으면 0.</summary>
        private static float[] GetMaxScorePerClass(List<RtdetrDetection> detections)
        {
            var maxScore = new float[NumDefectClasses];
            if (detections == null) return maxScore;

            foreach (var detection in detections)
            {
                if (detection.ClassId < 0 || detection.ClassId >= NumDefectClasses)
                    continue; // 방어: 스펙 밖 클래스 id는 무시

                if (detection.Score > maxScore[detection.ClassId])
                    maxScore[detection.ClassId] = detection.Score;
            }

            return maxScore;
        }

        /// <summary>
        /// SegFormer 픽셀맵에서 결함 클래스별 면적률(0~1)을 계산한다.
        /// SegFormer 클래스 id는 배경이 0번을 차지하므로 RT-DETR 기준 id + 1로 접근한다
        /// (ai/export/model_io_spec.md 4절).
        /// </summary>
        private static float[] GetAreaRatioPerClass(SegformerResult segmentation)
        {
            var areaRatio = new float[NumDefectClasses];
            if (segmentation?.ClassMap == null || segmentation.ClassMap.Length == 0)
                return areaRatio;

            var pixelCounts = new int[NumSegClasses];
            foreach (int segClassId in segmentation.ClassMap)
            {
                if (segClassId >= 0 && segClassId < NumSegClasses)
                    pixelCounts[segClassId]++;
            }

            int totalPixels = segmentation.ClassMap.Length;
            for (int classId = 0; classId < NumDefectClasses; classId++)
                areaRatio[classId] = pixelCounts[classId + 1] / (float)totalPixels; // +1 shift

            return areaRatio;
        }

        /// <summary>
        /// SegFormer만 잡은 결함의 confidence 근사치.
        /// 면적률이 커질수록 확신이 높다고 보되, 실제 확률이 아니므로 상한을 넘지 않게 한다.
        /// 로그 스케일이라 아주 작은 면적(노이즈 수준)은 낮은 값에 머문다.
        /// </summary>
        private static float EstimateConfidenceFromArea(float areaRatio)
        {
            float normalized = Mathf.Clamp01(Mathf.Log10(1f + areaRatio * 99f) / 2f);
            return normalized * SegmentationOnlyMaxConfidence;
        }
    }
}
