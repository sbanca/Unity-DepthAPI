using UnityEditor;
using UnityEngine;

namespace PassthroughCameraSamples.Editor
{
    [CustomEditor(typeof(WebCamTextureManager))]
    public class WebCamTextureManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty _eyeProp;
        private SerializedProperty _presetProp;
        private SerializedProperty _requestedResProp;
        private SerializedProperty _permissionsProp;

        private void OnEnable()
        {
            _eyeProp = serializedObject.FindProperty("Eye");
            _presetProp = serializedObject.FindProperty("requestedResolutionPreset");
            _requestedResProp = serializedObject.FindProperty("RequestedResolution");
            _permissionsProp = serializedObject.FindProperty("CameraPermissions");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_eyeProp);
            EditorGUILayout.PropertyField(_presetProp);

            var preset = (PassthroughCameraResolutionPreset)_presetProp.enumValueIndex;
            if (preset == PassthroughCameraResolutionPreset.Custom)
            {
                EditorGUILayout.PropertyField(_requestedResProp);
            }
            else if (preset == PassthroughCameraResolutionPreset.Auto)
            {
                EditorGUILayout.HelpBox("Auto selects the maximum supported resolution.", MessageType.Info);
            }
            else
            {
                var size = preset.ToVector2Int();
                EditorGUILayout.LabelField("RequestedResolution", $"{size.x}x{size.y}");
            }

            EditorGUILayout.PropertyField(_permissionsProp);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
