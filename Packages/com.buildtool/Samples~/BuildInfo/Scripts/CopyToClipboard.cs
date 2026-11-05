using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BuildTool.Samples.BuildInfo
{
    public class CopyToClipboard : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private TMP_Text _text;

        public TMP_Text Text
        {
            get => _text;
            set => _text = value;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_text != null)
            {
                GUIUtility.systemCopyBuffer = _text.text;
            }
        }
    }
}
