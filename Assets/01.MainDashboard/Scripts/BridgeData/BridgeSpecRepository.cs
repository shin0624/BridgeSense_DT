using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace BridgeSenseDT.BridgeData
{
    /// <summary>
    /// StreamingAssets에 들어있는 교량 제원 자료를 읽고 교량명·주소로 조회한다.
    ///
    /// 자료가 4만 건이 넘으므로 처음 조회할 때 한 번만 읽고 이후에는 메모리의 색인을 쓴다.
    /// 앱 시작 시점에 읽지 않는 이유는, 3D 뷰어를 열지 않는 사용자에게는 필요 없는 비용이기 때문이다.
    /// </summary>
    public static class BridgeSpecRepository
    {
        private const string RelativePath = "BridgeData/bridges.json";

        private static Dictionary<string, List<BridgeSpec>> indexByName;
        private static BridgeSpecTable table;
        private static bool loadAttempted;

        public static bool IsLoaded => indexByName != null;

        /// <summary>어떤 원본에서 만들어진 자료인지. 화면에 표시하거나 로그로 확인할 때 쓴다.</summary>
        public static string SourceName => table != null ? table.sourceName : null;

        /// <summary>
        /// 교량명과 주소로 제원을 찾는다. 찾지 못하면 null.
        ///
        /// 교량명이 같은 교량이 전국에 여럿 있을 수 있어(하천명을 딴 이름은 특히 흔하다)
        /// 이름만으로는 확정할 수 없다. 이름으로 후보를 좁힌 뒤 주소로 가려낸다.
        /// </summary>
        public static BridgeSpec Find(string bridgeName, string address)
        {
            EnsureLoaded();

            if (indexByName == null || string.IsNullOrWhiteSpace(bridgeName))
                return null;

            if (!indexByName.TryGetValue(Normalize(bridgeName), out var candidates) || candidates.Count == 0)
                return null;

            if (candidates.Count == 1)
                return candidates[0];

            return PickBestByAddress(candidates, address);
        }

        /// <summary>
        /// 후보 중 사용자가 입력한 주소와 가장 많이 겹치는 것을 고른다.
        /// 시도·시군구·읍면동을 각각 확인해 겹치는 단계가 많을수록 높은 점수를 준다.
        /// </summary>
        private static BridgeSpec PickBestByAddress(List<BridgeSpec> candidates, string address)
        {
            string normalizedAddress = Normalize(address);

            BridgeSpec best = candidates[0];
            int bestScore = -1;

            foreach (var candidate in candidates)
            {
                int score = 0;

                if (Contains(normalizedAddress, candidate.sido)) score++;
                if (Contains(normalizedAddress, candidate.sgg)) score++;
                if (Contains(normalizedAddress, candidate.emd)) score++;
                if (Contains(normalizedAddress, candidate.ri)) score++;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool Contains(string normalizedAddress, string part)
        {
            return !string.IsNullOrWhiteSpace(part)
                && normalizedAddress.Contains(Normalize(part));
        }

        /// <summary>띄어쓰기 차이로 조회가 실패하지 않도록 공백을 지우고 비교한다.</summary>
        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(" ", string.Empty);
        }

        /// <summary>
        /// 자료를 미리 읽어둔다. StartScene의 로딩 화면에서 호출한다.
        ///
        /// 이 클래스는 static이라 씬이 바뀌어도 읽어둔 색인이 그대로 남는다.
        /// 미리 부르지 않으면 3D 뷰어를 처음 열 때 8MB를 파싱하느라 화면이 한 번 멈춘다.
        /// </summary>
        public static void Preload()
        {
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (loadAttempted)
                return;

            loadAttempted = true; // 파일이 없을 때 조회할 때마다 다시 시도하지 않도록 한다

            string path = Path.Combine(Application.streamingAssetsPath, RelativePath);

            if (!File.Exists(path))
            {
                Debug.LogWarning(
                    $"교량 제원 자료를 찾지 못했습니다: {path}\n" +
                    "Unity 메뉴의 BridgeSense > Data > 교량 제원 데이터 생성을 먼저 실행해 주세요.");
                return;
            }

            try
            {
                table = JsonConvert.DeserializeObject<BridgeSpecTable>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"교량 제원 자료를 읽지 못했습니다: {e.Message}");
                return;
            }

            BuildIndex();
        }

        private static void BuildIndex()
        {
            indexByName = new Dictionary<string, List<BridgeSpec>>();

            if (table?.bridges == null)
                return;

            foreach (var spec in table.bridges)
            {
                string key = Normalize(spec.name);
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!indexByName.TryGetValue(key, out var list))
                {
                    list = new List<BridgeSpec>(1);
                    indexByName[key] = list;
                }

                list.Add(spec);
            }

            Debug.Log($"교량 제원 자료를 읽었습니다: {table.bridges.Count:N0}건 (원본 {table.sourceName})");
        }

        /// <summary>자료를 다시 읽도록 강제한다. 에디터에서 자료를 새로 만든 뒤 확인할 때 쓴다.</summary>
        public static void Reload()
        {
            loadAttempted = false;
            indexByName = null;
            table = null;
            EnsureLoaded();
        }
    }
}
