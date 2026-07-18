using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEditor;

namespace LKHGames
{
    public class GradientGenerator : MonoBehaviour
    {
        public Gradient gradient;
        [Tooltip("Input the saving path here. Changing the saving path is recommended incase of updates require deletion of the plugin.")]
        public string savingPath = "/LKHGames/GradientTextureGenerator/GeneratedTexture/";

        [Tooltip("Width of the gradient texture, 256 by default")]
        public float width = 256;
        [Tooltip("Height of the gradient texture, 64 by default")]
        public float height = 64;
        [Tooltip("Rotate gradient sampling by 90 degrees (vertical).")]
        public bool rotate90 = false;

        private Texture2D _gradientTexture;
        private static Texture2D _tempTexture;

        public enum OnPlayMode {Off, UpdateOnStart, UpdateEveryFrame};
        [Header("Material Properties")]
        public OnPlayMode onPlayMode;
        public string propertiesName;
        public Renderer materialRenderer;

        public enum TextureFormat {Png, Jpg};
        [Header("Texture Baker")]
        public TextureFormat textureFormat;

        void Start()
        {
            switch (onPlayMode)
            {
                case OnPlayMode.Off:
                    break;
                case OnPlayMode.UpdateOnStart:
                    UpdateGradientTexture();
                    break;
                case OnPlayMode.UpdateEveryFrame:
                    break;
            }
        }

        void Update()
        {
            switch (onPlayMode)
            {
                case OnPlayMode.Off:
                    break;
                case OnPlayMode.UpdateOnStart:
                    break;
                case OnPlayMode.UpdateEveryFrame:
                    UpdateGradientTexture();
                    break;
            }
        }

        public static Texture2D GenerateGradientTexture(Gradient grad, float width, float height, bool rotate90 = false)
        {
            int texWidth = (int)width;
            int texHeight = (int)height;
            _tempTexture = new Texture2D(texWidth, texHeight);

            float widthDenominator = Mathf.Max(1f, texWidth - 1f);
            float heightDenominator = Mathf.Max(1f, texHeight - 1f);

            for (int x = 0; x < texWidth; x++)
            {
                for (int y = 0; y < texHeight; y++)
                {
                    float t = rotate90 ? (y / heightDenominator) : (x / widthDenominator);
                    Color color = grad.Evaluate(t);
                    _tempTexture.SetPixel(x, y, color);
                }
            }
            _tempTexture.wrapMode = TextureWrapMode.Clamp;
            _tempTexture.Apply();
            return _tempTexture;
        }

        public void UpdateGradientTexture()
        {
            if(materialRenderer!=null)
            {
                _gradientTexture = GenerateGradientTexture(gradient, width, height, rotate90);
                materialRenderer.material.SetTexture(propertiesName, _gradientTexture);
            }
        }

        public void BakeGradientTexture()
        {
            string saveFormat = ".png";

            switch (textureFormat)
            {
                case TextureFormat.Png:
                    saveFormat = ".png";
                    break;
                case TextureFormat.Jpg:
                    saveFormat = ".jpg";
                    break;
            }
            
            _gradientTexture = GenerateGradientTexture(gradient, width, height, rotate90);
            byte[] _bytes = _gradientTexture.EncodeToPNG();

            #region Create new folder if it doesn't exist
			if(AssetDatabase.IsValidFolder("Assets/" + savingPath) == false)
			{
				string[] folderNameArray = savingPath.Split('/');
				string newfolderPath = "";

				for(int i = 0; i < folderNameArray.Length-2; i++)
				{	
					newfolderPath += folderNameArray[i];
					if(i != folderNameArray.Length-3)
					{
						newfolderPath += "/";
					}
				}
				AssetDatabase.CreateFolder("Assets" + newfolderPath, folderNameArray[folderNameArray.Length-2]);
				Debug.Log("<color=#FFFF00><b>Path saving location not found, New folder was created</b></color>");
			}
			#endregion

            var randomIndex = Random.Range(0, 999999).ToString();
            File.WriteAllBytes(Application.dataPath + savingPath + "GradientTexture_" + randomIndex + saveFormat, _bytes);
            Debug.Log("<color=#00FF00><b> GradientTexture_" + randomIndex + saveFormat + " baked sucessfully. Saved in the following path: " + "Assets" + savingPath + "</b></color>");
        }
    }
}