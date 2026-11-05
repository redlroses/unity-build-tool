using BuildTool.Tracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuildTool.Samples.BuildInfo
{
    public class BuildInfoDemoUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _productAndVersionText;
        [SerializeField] private TMP_Text _buildIndexText;
        [SerializeField] private TMP_Text _gitCommitText;
        [SerializeField] private TMP_Text _buildDateText;
        [SerializeField] private TMP_Text _platformText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private BuildInfoView _customFormatView;
        [SerializeField] private GameObject _watermarkOverlay;
        [SerializeField] private Button _copyClipboardButton;
        [SerializeField] private Button _toggleWatermarkButton;

        private void Awake()
        {
            if (_copyClipboardButton != null)
            {
                _copyClipboardButton.onClick.AddListener(OnCopyClicked);
            }

            if (_toggleWatermarkButton != null)
            {
                _toggleWatermarkButton.onClick.AddListener(OnToggleWatermarkClicked);
            }
        }

        private void Start()
        {
            RefreshDisplay();
        }

        private void OnDestroy()
        {
            if (_copyClipboardButton != null)
            {
                _copyClipboardButton.onClick.RemoveListener(OnCopyClicked);
            }

            if (_toggleWatermarkButton != null)
            {
                _toggleWatermarkButton.onClick.RemoveListener(OnToggleWatermarkClicked);
            }
        }

        public void RefreshDisplay()
        {
            if (_productAndVersionText != null)
            {
                _productAndVersionText.text = $"{Application.productName} v{Application.version}";
            }

            if (_platformText != null)
            {
                _platformText.text = Application.platform.ToString();
            }

            var info = BuildInfoProvider.BuildInfo;
            if (BuildInfoProvider.IsRegistered)
            {
                if (_buildIndexText != null)
                {
                    _buildIndexText.text = info.Index;
                }
                if (_gitCommitText != null)
                {
                    _gitCommitText.text = info.Commit;
                }
                if (_buildDateText != null)
                {
                    _buildDateText.text = info.Date;
                }
                if (_statusText != null)
                {
                    _statusText.text = "<color=green>Registered Build</color>";
                }
            }
            else
            {
                if (_buildIndexText != null)
                {
                    _buildIndexText.text = "N/A (Editor / Untracked)";
                }
                if (_gitCommitText != null)
                {
                    _gitCommitText.text = "N/A";
                }
                if (_buildDateText != null)
                {
                    _buildDateText.text = "N/A";
                }
                if (_statusText != null)
                {
                    _statusText.text = "<color=orange>Unregistered (Dev / Editor)</color>";
                }
            }

            if (_customFormatView != null)
            {
                _customFormatView.Refresh();
            }
        }

        private void OnCopyClicked()
        {
            if (_customFormatView != null)
            {
                GUIUtility.systemCopyBuffer = _customFormatView.FormatInfo(BuildInfoProvider.BuildInfo);
            }
        }

        private void OnToggleWatermarkClicked()
        {
            if (_watermarkOverlay != null)
            {
                _watermarkOverlay.SetActive(!_watermarkOverlay.activeSelf);
            }
        }
    }
}
