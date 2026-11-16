using System;
using BuildTool.Tracking;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace BuildTool.Samples.BuildInfo
{
    public class BuildInfoView : MonoBehaviour
    {
        private const string DefaultFormat = "{0} v{1} (build:{2}:{3}:{4}) {5}";
        private const string ErrorFormatInfo = "Incorrect build info format.";
        private const string NotForSharingDescription = BuildInfoProvider.NotForSharingDescription;
        private static readonly string RichNotForSharingDescription =
            $"\n<color=red>{NotForSharingDescription.Trim()}</color>";

        [SerializeField] private TMP_Text _text;
        [SerializeField] private string _format = DefaultFormat;
        [SerializeField] private bool _useRichText = true;
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private UnityEvent<string> _onBuildInfoLoaded;

        public TMP_Text Text
        {
            get => _text;
            set => _text = value;
        }

        public string Format
        {
            get => _format;
            set => _format = value;
        }

        public bool UseRichText
        {
            get => _useRichText;
            set => _useRichText = value;
        }

        public bool DontDestroyOnLoadEnabled
        {
            get => _dontDestroyOnLoad;
            set => _dontDestroyOnLoad = value;
        }

        public UnityEvent<string> OnBuildInfoLoaded => _onBuildInfoLoaded;

        private void Awake()
        {
            if (_dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Start()
        {
            Refresh();
        }

        public void Refresh()
        {
            var formatted = FormatInfo(BuildInfoProvider.BuildInfo);
            if (_text != null)
            {
                _text.text = formatted;
            }

            _onBuildInfoLoaded?.Invoke(formatted);
        }

        public string FormatInfo(BuildTool.Tracking.BuildInfo info)
        {
            var format = string.IsNullOrWhiteSpace(_format) ? DefaultFormat : _format;

            try
            {
                if (info != null && info.IsRegistered)
                {
                    return string.Format(
                        format,
                        Application.productName,
                        Application.version,
                        info.Index,
                        info.Commit,
                        info.Date,
                        Application.platform
                    );
                }

                return string.Format(
                           format,
                           Application.productName,
                           Application.version,
                           "N/A",
                           "N/A",
                           "N/A",
                           Application.platform) +
                       (_useRichText ? RichNotForSharingDescription : NotForSharingDescription);
            }
            catch (FormatException ex)
            {
                Debug.LogError($"Invalid format string: \"{format}\". {ex.Message}");
                return ErrorFormatInfo;
            }
        }
    }
}
