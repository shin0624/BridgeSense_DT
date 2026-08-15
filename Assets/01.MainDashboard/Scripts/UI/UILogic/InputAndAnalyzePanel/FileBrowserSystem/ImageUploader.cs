using UnityEngine;
using System.Collections;
using System.IO;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using SFB;
using TMPro;

public class ImageUploader : MonoBehaviour, IDropHandler
{   
    [SerializeField] private RawImage thumbnailImage; // 업로드된 이미지를 표시할 RawImage 컴포넌트
    [SerializeField] private Button fileBrowserButton;// "파일 선택 " 버튼
    [SerializeField] private TextMeshProUGUI errorText; // 오류 메시지를 표시할 TextMeshProUGUI 컴포넌트
    void Start()
    {
        if(fileBrowserButton != null)
        {
            fileBrowserButton.onClick.AddListener(OpenFileBrowser);
        }
    }

    public void OpenFileBrowser()// "파일 선택" 버튼 클릭시 브라우저 탐색기를 여는 메서드
    {
        var extensions = new[]
        {
            new ExtensionFilter("Image Files", "png", "jpg", "jpeg"), // 이미지 파일 확장자 필터
        };

        string[] paths = StandaloneFileBrowser.OpenFilePanel("BridgeSense DT_분석 대상 교량 이미지 선택", "", extensions, false);// 브라우저 탐색기에서 선택된 파일 경로를 가져옴

        if(paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))// 선택된 파일 경로가 유효한 경우
        {
            StartCoroutine(LoadImageRoutine(paths[0])); // 선택된 파일 경로를 LoadImageRoutine 코루틴에 전달하여 이미지 로드
        }
    }

    public void OnDrop(PointerEventData eventData) // 드래그앤 드롭을 위한 IDropHandler 인터페이스 구현
    {
        
    }

    private void OnEnable() // 파일 드래그 드롭 이벤트를 수신하는 메서드
    {
        
    }

    private IEnumerator LoadImageRoutine(string filePath)// 이미지파일을 텍스처로 로드하여 UI에 적용하는 메서드
    {
        string url = "file:///" + filePath; // 파일 경로 보정(file:///) 후 URL 생성

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))// url 이미지에서 HTTP로 이미지 텍스처를 가져온다.
        {
            yield return www.SendWebRequest();// 웹 요청 완료까지 대기

            if(www.result == UnityWebRequest.Result.Success)// 요청 성공 시
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);// 텍스처를 가져온다.
                thumbnailImage.texture = texture;// RawImage 텍스처에 url에서 가져온 텍스처를 할당한다.
                thumbnailImage.SetNativeSize();// RawImage의 크기를 텍스처의 원본 크기로 설정한다.
                
            }
            else
            {
                Debug.LogError("이미지 로드 실패: " + www.error);
                errorText.text = "이미지 파일 로드에 실패하였습니다. 다시 시도해 주세요.\n" + "오류 코드 : " + www.error; // 오류 메시지를 UI에 표시
            }
        }
    }
}
