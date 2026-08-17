using UnityEngine;
using UnityEngine.UI;

public class InputImageObject : MonoBehaviour
{
    // InputImageObject는 InputAndAnalyzePanel에서 사용되는 프리팹으로, 이미지 업로더 + 교량 정보 입력필드 + 이미지 및 정보 등록 완료 버튼을 하나로 묶은 UI 오브젝트를 나타낸다.
    // 받은 데이터를 화면에 표시만 하도록 하고, 실제 데이터 처리는 BridgeImageRegistrationController에서 수행한다.
    private Button deleteButton; // 삭제 버튼

    public void Initialize(Texture thumbnail, int id, string bridgeName, string location, string capturedPart )
    {
        // 초기화 메서드 - 프리팹이 생성될 때 호출되어 UI 요소들을 초기 상태로 설정한다.
        // thumbnail: 업로드된 이미지의 썸네일 텍스처
        // id: 교량 ID
        // bridgeName: 교량 이름
        // location: 교량 위치
        // capturedPart: 촬영 부위
        deleteButton = GetComponentInChildren<Button>(); // 삭제 버튼 컴포넌트 가져오기
        deleteButton.onClick.AddListener(OnDeleteButtonClicked); // 삭제 버튼 클릭 시 OnDeleteButtonClicked 메서드 호출하도록 리스너 등록

    }

    private void OnDeleteButtonClicked() // 삭제 버튼 클릭 시 호출되는 메서드. 이 오브젝트를 삭제하고 BridgeImageRegistrationController에 알린다.
    {
        deleteButton.onClick.RemoveListener(OnDeleteButtonClicked); // 리스너 제거
        Destroy(this.gameObject);

    }
}
