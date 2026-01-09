using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DepthFrustumVisualizer))]
public class DepthFrustumVisualizerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var visualizer = (DepthFrustumVisualizer)target;
        if (GUILayout.Button("Build Frustum"))
        {
            visualizer.BuildFrustum();
            EditorUtility.SetDirty(visualizer);
        }
    }
}
