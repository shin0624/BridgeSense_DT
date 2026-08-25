using System.Collections.Generic;

namespace BridgeSenseDT.BridgeData
{
    /// <summary>
    /// 국토교통부 "도로 교량 및 터널 현황조서"에서 화면 표시에 필요한 항목만 추린 교량 제원 1건.
    ///
    /// 필드명을 짧게 둔 이유:
    /// 전국 교량이 4만 건이 넘어서 키 이름 길이가 그대로 파일 크기로 이어진다.
    /// 원본 조서의 컬럼명과의 대응은 각 필드 주석에 적어둔다.
    /// </summary>
    public class BridgeSpec
    {
        public string name;   // 시설명
        public string sido;   // 시도
        public string sgg;    // 시군구
        public string emd;    // 읍면동
        public string ri;     // 리
        public string agency; // 기관구분1~3을 하나로 합친 관리기관
        public string sup;    // 상부구조
        public string sub;    // 하부구조
        public string year;   // 준공년도

        public string len;    // 총길이(m) - 입면도의 총 교장
        public string spans;  // 경간수
        public string maxSpan;// 최대경간장(m)

        /// <summary>시도부터 리까지를 이어붙인 주소. 비어있는 단계는 건너뛴다.</summary>
        public string GetAddress()
        {
            var parts = new List<string>(4);

            if (!string.IsNullOrWhiteSpace(sido)) parts.Add(sido);
            if (!string.IsNullOrWhiteSpace(sgg)) parts.Add(sgg);
            if (!string.IsNullOrWhiteSpace(emd)) parts.Add(emd);
            if (!string.IsNullOrWhiteSpace(ri)) parts.Add(ri);

            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// 저장 파일의 최상위 구조. 언제 어떤 원본에서 만들어졌는지 함께 기록한다.
    /// 해마다 새 현황조서로 교체할 때 지금 들어있는 것이 몇 년도 자료인지 확인할 수 있어야 한다.
    /// </summary>
    public class BridgeSpecTable
    {
        public string sourceName;   // 원본 파일명
        public string generatedAt;  // 생성 시각(ISO8601)
        public List<BridgeSpec> bridges = new List<BridgeSpec>();
    }
}
