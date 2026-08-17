using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class BridgeImageRegistrationController : MonoBehaviour
{
    // 이미지 업로더 + 교량 정보 입력필드 + 이미지 및 정보 등록 완료 버튼을 하나로 묶어서 InputImageObject 프리팹을 생성하는 컨트롤러.
    
    [SerializeField] private TMP_InputField bridgeNameInputField; // 교량 이름 입력 필드
    [SerializeField] private TMP_InputField locationInputField; // 교량 위치 입력 필드
    [SerializeField] private TMP_InputField capturedPartInputField; // 촬영 부위 입력 필드
    [SerializeField] private GameObject inputImageObjectPrefab; // InputImageObject 프리팹
    [SerializeField] private Button registerButton; // "이미지 및 정보 등록 완료" 버튼
    [SerializeField] private Transform inputImageObjectParent; // InputImageObject 프리팹이 생성될 부모 오브젝트
    [SerializeField] private ImageUploader imageUploader; // 이미지 업로더 스크립트
    [SerializeField] private GameObject registerInvalidPopup;
    private int objectInstanceCounter;
    private bool isImageUploaded = false; // 이미지 업로드 여부를 추적하는 플래그
    
    void Start()
    {
        objectInstanceCounter = 0;
        registerButton.onClick.AddListener(OnRegisterButtonClicked); // "이미지 및 정보 등록 완료" 버튼 클릭 시 OnRegisterButtonClicked 메서드 호출하도록 리스너 등록
    }

    private void OnRegisterButtonClicked()// "이미지 및 정보 등록 완료" 버튼 클릭 시 호출되는 메서드. 아래의 메인 메서드들을 순서대로 호출하는 오케스트레이터 역할
    {
        if(!isInputValid())
        {
            registerInvalidPopup.SetActive(true);
            return;
        }
        else
        {
            CreateInputImageObject();// InputImageObject 생성
            LockBridgeInfoFields();// 우선 한개의 교량에 대해서만 입력을 받기 위해, Lock 수행
            ResetForNextEntry();// 다음 교량이미지 입력
        }
    }

    private bool isInputValid()//교량 이미지, 교량 정보 등이 null일 경우를 체크하는 메서드
    {
        if(imageUploader.GetCurrentThumbnailTexture()==null || bridgeNameInputField.text==null || locationInputField == null || capturedPartInputField == null)
        {  
            return false;
        }
        else
        {

            return true;
        }
    }

    private void CreateInputImageObject()// InputImageObject를 생성하는 메서드
    {
        GameObject prefabInstance = Instantiate(inputImageObjectPrefab, inputImageObjectParent);
        if(prefabInstance!=null)
        {
            objectInstanceCounter++;
            InputImageObject objectInstance = prefabInstance.GetComponent<InputImageObject>();
            objectInstance.Initialize(imageUploader.GetCurrentThumbnailTexture(), objectInstanceCounter, bridgeNameInputField.text, locationInputField.text, capturedPartInputField.text);
        }
        

    }

    private void LockBridgeInfoFields()
    {
        
    }

    private void ResetForNextEntry()
    {
        
    }

    private void OnDisable() {
        registerButton.onClick.RemoveListener(OnRegisterButtonClicked); // 리스너 제거
    }
}
