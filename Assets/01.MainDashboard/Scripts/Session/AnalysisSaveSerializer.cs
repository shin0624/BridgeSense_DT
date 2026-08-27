using System;
using System.Collections.Generic;
using System.IO;
using BridgeSenseDT.Assessment;
using Newtonsoft.Json;
using UnityEngine;

namespace BridgeSenseDT.Session
{
    /// <summary>
    /// AnalysisSession(런타임 모델)과 AnalysisSaveFile(저장 스키마) 사이를 변환하고,
    /// Newtonsoft.Json으로 .bsdt 파일을 읽고 쓴다.
    ///
    /// JsonUtility가 아니라 Newtonsoft를 쓰는 이유:
    /// JsonUtility는 프로퍼티를 직렬화하지 못하고 Dictionary와 최상위 배열도 다루지 못해
    /// DTO를 훨씬 많이 만들어야 한다. Newtonsoft는 com.unity.visualscripting이 이미
    /// 의존성으로 끌어와 있어 패키지를 새로 추가할 필요가 없다.
    /// </summary>
    public static class AnalysisSaveSerializer
    {
        public const int CurrentFormatVersion = 1;

        public const string FileExtension = "bsdt";       // 확장자(점 없음). 파일 다이얼로그 필터에 그대로 쓴다
        public const string FileExtensionWithDot = ".bsdt";

        /// <summary>
        /// 이 결과를 만들어낸 AI 모델의 식별자.
        /// 모델을 재학습해 .onnx를 교체하면 이 문자열도 함께 올릴 것.
        /// 그래야 과거 저장본의 등급이 어떤 모델 기준이었는지 나중에 판별할 수 있다.
        /// </summary>
        public const string CurrentModelVersion = "rtdetr-v2-r18vd@260810";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,          // 저장 파일을 직접 열어 확인할 수 있게 함
            NullValueHandling = NullValueHandling.Ignore, // 분석 전 세션의 snapshot 같은 null 필드를 생략
        };

        public static void SaveToFile(AnalysisSession session, string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory); // 기본 저장 폴더가 아직 없을 수 있으므로 미리 만든다

            File.WriteAllText(filePath, Serialize(session));
        }

        public static AnalysisSession LoadFromFile(string filePath)
        {
            return Deserialize(File.ReadAllText(filePath));
        }

        public static string Serialize(AnalysisSession session)
        {
            return JsonConvert.SerializeObject(ToSaveFile(session), Settings);
        }

        public static AnalysisSession Deserialize(string json)
        {
            var saveFile = JsonConvert.DeserializeObject<AnalysisSaveFile>(json, Settings);
            if (saveFile == null)
                throw new InvalidDataException("저장 파일을 해석하지 못했습니다. 내용이 비어있거나 형식이 올바르지 않습니다.");

            if (saveFile.formatVersion > CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"이 저장 파일은 더 새로운 버전(formatVersion {saveFile.formatVersion})으로 만들어졌습니다. " +
                    $"현재 프로그램이 읽을 수 있는 최대 버전은 {CurrentFormatVersion}입니다.");
            }

            return FromSaveFile(saveFile);
        }

        public static AnalysisSaveFile ToSaveFile(AnalysisSession session)
        {
            var saveFile = new AnalysisSaveFile
            {
                formatVersion = CurrentFormatVersion,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                modelVersion = CurrentModelVersion,
                bridge = new BridgeInfoDto
                {
                    name = session.BridgeName,
                    location = session.Location,
                },
            };

            foreach (var entry in session.Entries)
                saveFile.entries.Add(ToEntryDto(entry));

            if (session.Snapshot != null)
            {
                saveFile.assessmentSnapshot = new AssessmentSnapshotDto
                {
                    grade = session.Snapshot.Grade,
                    totalScore = session.Snapshot.TotalScore,
                    majorScore = session.Snapshot.MajorScore,
                    generalScore = session.Snapshot.GeneralScore,
                    ancillaryScore = session.Snapshot.AncillaryScore,
                };
            }

            return saveFile;
        }

        public static AnalysisSession FromSaveFile(AnalysisSaveFile saveFile)
        {
            var session = new AnalysisSession
            {
                BridgeName = saveFile.bridge?.name ?? "",
                Location = saveFile.bridge?.location ?? "",
            };

            if (saveFile.entries != null)
            {
                foreach (var entryDto in saveFile.entries)
                    session.Entries.Add(FromEntryDto(entryDto));
            }

            if (saveFile.assessmentSnapshot != null)
            {
                session.Snapshot = new AssessmentSnapshot
                {
                    Grade = saveFile.assessmentSnapshot.grade,
                    TotalScore = saveFile.assessmentSnapshot.totalScore,
                    MajorScore = saveFile.assessmentSnapshot.majorScore,
                    GeneralScore = saveFile.assessmentSnapshot.generalScore,
                    AncillaryScore = saveFile.assessmentSnapshot.ancillaryScore,
                };
            }

            // 분석 결과가 하나라도 있으면 결과 화면 상태로, 아니면 편집 상태로 복원한다.
            session.State = HasAnyAnalyzedEntry(session)
                ? AnalysisSessionState.Analyzed
                : AnalysisSessionState.Editing;

            return session;
        }

        private static bool HasAnyAnalyzedEntry(AnalysisSession session)
        {
            foreach (var entry in session.Entries)
            {
                if (entry.Analyzed)
                    return true;
            }
            return false;
        }

        private static EntryDto ToEntryDto(AnalysisEntry entry)
        {
            var dto = new EntryDto
            {
                entryId = entry.EntryId,
                capturedPart = entry.CapturedPart,
                imageFileName = entry.ImageFileName,
                imageData = entry.ImageBytes,
                analyzed = entry.Analyzed,
            };

            foreach (var detection in entry.Detections)
            {
                dto.detections.Add(new DetectionDto
                {
                    classId = detection.ClassId,
                    score = detection.Score,
                    x1 = detection.X1,
                    y1 = detection.Y1,
                    x2 = detection.X2,
                    y2 = detection.Y2,
                });
            }

            foreach (var defect in entry.Defects)
            {
                var defectDto = new DefectDto
                {
                    defectType = defect.type.ToString(),
                    confidence = defect.confidence,
                    estimatedWidthMm = defect.estimatedWidthMm,
                    isStructurallyCritical = defect.isStructurallyCritical,
                };

                if (defect.boxes != null)
                {
                    foreach (var box in defect.boxes)
                    {
                        defectDto.boxes.Add(new DefectBoxDto
                        {
                            x1 = box.xMin,
                            y1 = box.yMin,
                            x2 = box.xMax,
                            y2 = box.yMax,
                        });
                    }
                }

                dto.defects.Add(defectDto);
            }

            return dto;
        }

        private static AnalysisEntry FromEntryDto(EntryDto dto)
        {
            var entry = new AnalysisEntry
            {
                EntryId = dto.entryId,
                CapturedPart = dto.capturedPart,
                ImageFileName = dto.imageFileName,
                ImageBytes = dto.imageData,
                Analyzed = dto.analyzed,
            };

            if (dto.detections != null)
            {
                foreach (var detectionDto in dto.detections)
                {
                    entry.Detections.Add(new RtdetrDetection
                    {
                        ClassId = detectionDto.classId,
                        Score = detectionDto.score,
                        X1 = detectionDto.x1,
                        Y1 = detectionDto.y1,
                        X2 = detectionDto.x2,
                        Y2 = detectionDto.y2,
                    });
                }
            }

            if (dto.defects != null)
            {
                foreach (var defectDto in dto.defects)
                {
                    // 알 수 없는 결함 이름은 해당 결함만 건너뛴다.
                    // 저장 파일 전체를 못 읽게 만드는 것보다 일부 손실을 감수하는 편이 낫다.
                    if (!Enum.TryParse(defectDto.defectType, out DefectType defectType))
                    {
                        Debug.LogWarning($"알 수 없는 결함 유형이라 건너뜁니다: {defectDto.defectType}");
                        continue;
                    }

                    var defect = new DetectedDefect
                    {
                        type = defectType,
                        confidence = defectDto.confidence,
                        estimatedWidthMm = defectDto.estimatedWidthMm,
                        isStructurallyCritical = defectDto.isStructurallyCritical,
                    };

                    // 사각형이 없는 저장본은 이 기능이 생기기 전에 만들어진 것이다.
                    // 나머지 값은 그대로 쓸 수 있으므로 사각형만 비운 채로 복원한다.
                    if (defectDto.boxes != null)
                    {
                        foreach (var boxDto in defectDto.boxes)
                        {
                            defect.boxes.Add(new DefectBox
                            {
                                xMin = boxDto.x1,
                                yMin = boxDto.y1,
                                xMax = boxDto.x2,
                                yMax = boxDto.y2,
                            });
                        }
                    }

                    entry.Defects.Add(defect);
                }
            }

            return entry;
        }

        /// <summary>저장 파일들이 기본으로 놓이는 폴더. 없으면 만들어서 반환한다.</summary>
        public static string GetDefaultSaveDirectory()
        {
            string directory = Path.Combine(Application.persistentDataPath, "Analyses");
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
